import { Callsign } from "./callsign.js";
import {
  type Ax25Frame,
  classify,
  decodeFrame,
  disc,
  dm,
  encodeFrame,
  getNr,
  getNs,
  iFrame,
  isCommand,
  pollFinal,
  rr,
  sabm,
  ua,
} from "./frame.js";
import type { Ax25Transport } from "./transport.js";

/**
 * Hand-rolled AX.25 v2.2 mod-8 connected-mode session driver.
 *
 * This is NOT a faithful port of figc4.1-4.7. It implements the happy paths
 * Tom asked for:
 *
 *   - SABM → UA → Connected
 *   - I-frame TX/RX with V(s)/V(r)/V(a) bookkeeping and RR acks
 *   - DISC → UA → Disconnected
 *   - T1 retry on SABM and DISC, capped at N2 (default 10)
 *
 * Out of scope (will not work / will throw / will silently drop):
 *   - mod-128 (SABME, extended sequence numbers)
 *   - REJ / SREJ recovery (we just ignore REJ; T1 retransmit covers most cases)
 *   - T1 dynamic adjustment (fixed 3-second default)
 *   - FRMR generation
 *   - Multi-frame windowing beyond k=1 (one outstanding I-frame at a time)
 *   - Digipeater paths — `via` triggers "not implemented" at construction time
 */
type SessionState =
  | "Disconnected"
  | "AwaitingConnection"
  | "Connected"
  | "AwaitingRelease";

const DEFAULT_T1_MS = 3000;
const DEFAULT_N2 = 10;
const DEFAULT_PID = 0xf0;

export interface Ax25SessionOptions {
  /** T1 retry timeout (ms). Default 3000. */
  t1Ms?: number;
  /** Maximum retries (N2). Default 10. */
  n2?: number;
  /** PID for outbound I-frames. Default 0xF0 (no L3 protocol). */
  pid?: number;
}

/**
 * One connected-mode AX.25 session. Created via {@link Ax25Stack.connect}.
 * Callers register data/disconnect listeners, push outbound bytes via
 * {@link Ax25Session.write}, and tear down the link via
 * {@link Ax25Session.disconnect}.
 */
export class Ax25Session {
  readonly from: Callsign;
  readonly to: Callsign;
  private readonly send: (frame: Ax25Frame) => Promise<void>;
  private readonly removeFromStack: () => void;
  private readonly opts: Required<Ax25SessionOptions>;

  private state: SessionState = "Disconnected";
  private vs = 0; // send state variable
  private va = 0; // ack state variable (last acked)
  private vr = 0; // receive state variable
  private retries = 0;

  /** Resolves when SABM exchange completes; rejects on N2 exhaustion. */
  private connectResolver: {
    resolve: () => void;
    reject: (err: Error) => void;
  } | null = null;
  /** Resolves when DISC exchange completes (UA received, or timer expired). */
  private disconnectResolver: { resolve: () => void } | null = null;

  /** Pending I-frame retransmit (k=1 window). null = none outstanding. */
  private pendingI: { ns: number; info: Uint8Array; pid: number } | null = null;
  /** Queue of payloads to send after the current outstanding I-frame is ack'd. */
  private txQueue: { info: Uint8Array; pid: number }[] = [];

  private t1Handle: ReturnType<typeof setTimeout> | null = null;
  private dataListeners: Array<(chunk: Uint8Array) => void> = [];
  private disconnectListeners: Array<() => void> = [];

  /** @internal — constructed only by Ax25Stack. */
  constructor(
    from: Callsign,
    to: Callsign,
    send: (frame: Ax25Frame) => Promise<void>,
    removeFromStack: () => void,
    opts: Ax25SessionOptions = {},
  ) {
    this.from = from;
    this.to = to;
    this.send = send;
    this.removeFromStack = removeFromStack;
    this.opts = {
      t1Ms: opts.t1Ms ?? DEFAULT_T1_MS,
      n2: opts.n2 ?? DEFAULT_N2,
      pid: opts.pid ?? DEFAULT_PID,
    };
  }

  /** Register a callback invoked when the peer delivers I-frame info. */
  onData(callback: (chunk: Uint8Array) => void): void {
    this.dataListeners.push(callback);
  }

  /** Register a callback invoked when the session enters Disconnected. */
  onDisconnected(callback: () => void): void {
    this.disconnectListeners.push(callback);
  }

  /**
   * Queue a payload for transmission. Resolves once the bytes have been
   * accepted into the local TX queue (NOT once the peer has ack'd — that
   * would require a much richer API). The session sends one I-frame at a
   * time; subsequent calls queue behind the outstanding frame.
   */
  async write(chunk: Uint8Array): Promise<void> {
    if (this.state !== "Connected") {
      throw new Error(`cannot write in state ${this.state}`);
    }
    if (chunk.length === 0) return;
    this.txQueue.push({ info: chunk, pid: this.opts.pid });
    await this.pumpTx();
  }

  /** Initiate disconnect. Resolves when the link is fully torn down. */
  async disconnect(): Promise<void> {
    if (this.state === "Disconnected") return;
    if (this.state === "AwaitingRelease") {
      return new Promise<void>((resolve) => {
        const prev = this.disconnectResolver;
        this.disconnectResolver = {
          resolve: () => {
            prev?.resolve();
            resolve();
          },
        };
      });
    }
    this.clearT1();
    this.state = "AwaitingRelease";
    this.retries = 0;
    const p = new Promise<void>((resolve) => {
      this.disconnectResolver = { resolve };
    });
    await this.sendDisc();
    return p;
  }

  /** @internal — called by Ax25Stack at session creation to start SABM. */
  async _initiateConnect(): Promise<void> {
    if (this.state !== "Disconnected") {
      throw new Error("session already started");
    }
    this.state = "AwaitingConnection";
    this.retries = 0;
    const p = new Promise<void>((resolve, reject) => {
      this.connectResolver = { resolve, reject };
    });
    await this.sendSabm();
    return p;
  }

  /** @internal — called by Ax25Stack for every inbound frame matching this peer. */
  _handleFrame(frame: Ax25Frame): void {
    const kind = classify(frame);
    switch (this.state) {
      case "AwaitingConnection":
        this.onFrameAwaitingConnection(frame, kind);
        break;
      case "Connected":
        this.onFrameConnected(frame, kind);
        break;
      case "AwaitingRelease":
        this.onFrameAwaitingRelease(frame, kind);
        break;
      case "Disconnected":
        // Stray frame after teardown. Reply DM if peer is still polling us.
        if (kind === "SABM" || kind === "DISC") {
          void this.send(
            dm({
              destination: frame.source.callsign,
              source: this.from,
              finalBit: pollFinal(frame),
            }),
          );
        }
        break;
    }
  }

  /** @internal — called by Ax25Stack when transport stops. */
  _forceDisconnect(): void {
    this.clearT1();
    if (this.state !== "Disconnected") {
      this.state = "Disconnected";
      this.connectResolver?.reject(new Error("transport stopped"));
      this.connectResolver = null;
      this.disconnectResolver?.resolve();
      this.disconnectResolver = null;
      for (const cb of this.disconnectListeners) cb();
    }
    this.removeFromStack();
  }

  // ─── State handlers ──────────────────────────────────────────────────

  private onFrameAwaitingConnection(frame: Ax25Frame, kind: string): void {
    if (kind === "UA") {
      // Connection established.
      this.clearT1();
      this.state = "Connected";
      this.vs = 0;
      this.va = 0;
      this.vr = 0;
      this.retries = 0;
      this.connectResolver?.resolve();
      this.connectResolver = null;
      // Now pump any I-frames queued before the link came up.
      void this.pumpTx();
    } else if (kind === "DM") {
      // Peer refused.
      this.clearT1();
      this.state = "Disconnected";
      this.connectResolver?.reject(
        new Error("peer refused connection (DM received)"),
      );
      this.connectResolver = null;
      for (const cb of this.disconnectListeners) cb();
      this.removeFromStack();
    } else if (kind === "SABM") {
      // Collision: peer also wants to connect. Respond UA and consider
      // ourselves connected.
      void this.send(
        ua({
          destination: frame.source.callsign,
          source: this.from,
          finalBit: pollFinal(frame),
        }),
      );
      this.clearT1();
      this.state = "Connected";
      this.vs = 0;
      this.va = 0;
      this.vr = 0;
      this.connectResolver?.resolve();
      this.connectResolver = null;
    }
    // Other frames ignored in this state.
  }

  private onFrameConnected(frame: Ax25Frame, kind: string): void {
    if (kind === "I") {
      const ns = getNs(frame);
      const nr = getNr(frame);
      this.handleAck(nr);
      if (ns === this.vr) {
        this.vr = (this.vr + 1) % 8;
        if (frame.info && frame.info.length > 0) {
          for (const cb of this.dataListeners) cb(frame.info);
        }
        // Send RR with updated N(R). If the peer polled us (P=1), set F=1.
        void this.send(
          rr({
            destination: frame.source.callsign,
            source: this.from,
            nr: this.vr,
            isCommand: false,
            pollFinal: pollFinal(frame),
          }),
        );
      } else {
        // Out-of-sequence I-frame. Per spec we'd send REJ; we send an
        // RR (no-update) which causes the peer's T1 to fire and they'll
        // retransmit. This is a known reduction from full v2.2.
        void this.send(
          rr({
            destination: frame.source.callsign,
            source: this.from,
            nr: this.vr,
            isCommand: false,
            pollFinal: pollFinal(frame),
          }),
        );
      }
    } else if (kind === "RR") {
      const nr = getNr(frame);
      this.handleAck(nr);
      // If peer is polling, respond with our own RR/F=1.
      if (pollFinal(frame) && isCommand(frame)) {
        void this.send(
          rr({
            destination: frame.source.callsign,
            source: this.from,
            nr: this.vr,
            isCommand: false,
            pollFinal: true,
          }),
        );
      }
    } else if (kind === "REJ") {
      // Reject. Spec says retransmit all I-frames from N(R) onwards.
      // Our k=1 simplification: just ack-advance and rely on T1 retransmit.
      const nr = getNr(frame);
      this.handleAck(nr);
      // Trigger an immediate retransmit if anything is still outstanding.
      if (this.pendingI !== null) {
        void this.retransmitI();
      }
    } else if (kind === "DISC") {
      // Peer is tearing down. Respond UA, enter Disconnected.
      void this.send(
        ua({
          destination: frame.source.callsign,
          source: this.from,
          finalBit: pollFinal(frame),
        }),
      );
      this.transitionToDisconnected();
    } else if (kind === "SABM") {
      // Peer is re-establishing. Per spec this resets the session.
      void this.send(
        ua({
          destination: frame.source.callsign,
          source: this.from,
          finalBit: pollFinal(frame),
        }),
      );
      this.vs = 0;
      this.va = 0;
      this.vr = 0;
      this.pendingI = null;
      this.txQueue = [];
    } else if (kind === "DM") {
      // Peer dropped link.
      this.transitionToDisconnected();
    }
  }

  private onFrameAwaitingRelease(frame: Ax25Frame, kind: string): void {
    if (kind === "UA" || kind === "DM") {
      // Both are valid responses to DISC.
      this.clearT1();
      this.transitionToDisconnected();
      this.disconnectResolver?.resolve();
      this.disconnectResolver = null;
    } else if (kind === "DISC") {
      // Cross-DISC: respond UA and proceed to disconnected.
      void this.send(
        ua({
          destination: frame.source.callsign,
          source: this.from,
          finalBit: pollFinal(frame),
        }),
      );
      this.clearT1();
      this.transitionToDisconnected();
      this.disconnectResolver?.resolve();
      this.disconnectResolver = null;
    }
    // Other frames ignored while we wait for UA.
  }

  // ─── TX pumps ────────────────────────────────────────────────────────

  private async pumpTx(): Promise<void> {
    if (this.state !== "Connected") return;
    if (this.pendingI !== null) return; // k=1 window already occupied
    const next = this.txQueue.shift();
    if (!next) return;
    const ns = this.vs;
    this.pendingI = { ns, info: next.info, pid: next.pid };
    this.vs = (this.vs + 1) % 8;
    this.retries = 0;
    await this.sendPendingI(true);
  }

  private async sendPendingI(armTimer: boolean): Promise<void> {
    if (!this.pendingI) return;
    const frame = iFrame({
      destination: this.to,
      source: this.from,
      nr: this.vr,
      ns: this.pendingI.ns,
      info: this.pendingI.info,
      pid: this.pendingI.pid,
      pollBit: true,
    });
    await this.send(frame);
    if (armTimer) this.armT1(() => this.onT1ExpiredIFrame());
  }

  private async retransmitI(): Promise<void> {
    if (!this.pendingI) return;
    await this.sendPendingI(true);
  }

  private handleAck(nr: number): void {
    // A peer N(R) acks frames with N(s) < N(R) (mod 8).
    if (
      this.pendingI !== null &&
      seqInWindow(this.pendingI.ns, this.va, nr)
    ) {
      this.va = nr;
      this.pendingI = null;
      this.clearT1();
      // Pump the next queued frame, if any.
      void this.pumpTx();
    } else {
      this.va = nr;
    }
  }

  // ─── Control transmissions + T1 management ──────────────────────────

  private async sendSabm(): Promise<void> {
    await this.send(
      sabm({ destination: this.to, source: this.from, pollBit: true }),
    );
    this.armT1(() => this.onT1ExpiredSabm());
  }

  private async sendDisc(): Promise<void> {
    await this.send(
      disc({ destination: this.to, source: this.from, pollBit: true }),
    );
    this.armT1(() => this.onT1ExpiredDisc());
  }

  private onT1ExpiredSabm(): void {
    if (this.state !== "AwaitingConnection") return;
    this.retries++;
    if (this.retries > this.opts.n2) {
      this.state = "Disconnected";
      this.connectResolver?.reject(
        new Error(`SABM retry limit (N2=${this.opts.n2}) exhausted`),
      );
      this.connectResolver = null;
      this.removeFromStack();
      return;
    }
    void this.sendSabm();
  }

  private onT1ExpiredDisc(): void {
    if (this.state !== "AwaitingRelease") return;
    this.retries++;
    if (this.retries > this.opts.n2) {
      // Per spec: declare disconnected even without UA.
      this.transitionToDisconnected();
      this.disconnectResolver?.resolve();
      this.disconnectResolver = null;
      return;
    }
    void this.sendDisc();
  }

  private onT1ExpiredIFrame(): void {
    if (this.state !== "Connected") return;
    if (!this.pendingI) return;
    this.retries++;
    if (this.retries > this.opts.n2) {
      // Link broken — force disconnect.
      this.transitionToDisconnected();
      return;
    }
    void this.retransmitI();
  }

  private armT1(onFire: () => void): void {
    this.clearT1();
    this.t1Handle = setTimeout(onFire, this.opts.t1Ms);
  }

  private clearT1(): void {
    if (this.t1Handle !== null) {
      clearTimeout(this.t1Handle);
      this.t1Handle = null;
    }
  }

  private transitionToDisconnected(): void {
    this.clearT1();
    this.state = "Disconnected";
    this.pendingI = null;
    this.txQueue = [];
    for (const cb of this.disconnectListeners) cb();
    this.removeFromStack();
  }
}

/**
 * Sequence-arithmetic helper: is `ns` in the half-open window (va, nr] mod 8?
 * Used to decide whether a peer N(R) acks our outstanding I-frame.
 */
function seqInWindow(ns: number, va: number, nr: number): boolean {
  // mod-8 distance from va to nr (forward, exclusive of va).
  const dist = (nr - va + 8) % 8;
  // distance from va to ns + 1 (since N(R) acks up to but not including).
  const want = (ns - va + 8) % 8;
  // ns is acked iff want < dist.
  return want < dist;
}

// ─── Stack: routes inbound frames to sessions, multiplexes outbound ─────

/**
 * Holds the transport, runs the inbound demux loop, and is the factory
 * for new Ax25Sessions. Designed for one-shot connect-and-stream usage —
 * see README.
 */
export class Ax25Stack {
  private readonly transport: Ax25Transport;
  private readonly sessions = new Map<string, Ax25Session>();
  private started = false;

  constructor(transport: Ax25Transport) {
    this.transport = transport;
  }

  async start(): Promise<void> {
    if (this.started) return;
    await this.transport.start((bytes) => this.onInboundFrame(bytes));
    this.started = true;
  }

  /**
   * Initiate a connected-mode session to `to`. Resolves with the session
   * once the SABM/UA handshake completes; rejects on N2 exhaustion or
   * peer DM.
   *
   * `via` is the digipeater path; it's NOT supported in v1 and will throw.
   */
  async connect(args: {
    from: string | Callsign;
    to: string | Callsign;
    via?: string[];
    options?: Ax25SessionOptions;
  }): Promise<Ax25Session> {
    if (!this.started) {
      throw new Error("stack not started — call start() first");
    }
    if (args.via && args.via.length > 0) {
      throw new Error("digipeater paths (`via`) are not implemented in v1");
    }
    const from =
      typeof args.from === "string" ? Callsign.parse(args.from) : args.from;
    const to = typeof args.to === "string" ? Callsign.parse(args.to) : args.to;
    const key = sessionKey(from, to);
    if (this.sessions.has(key)) {
      throw new Error(`session ${key} already exists`);
    }
    const session = new Ax25Session(
      from,
      to,
      (frame) => this.transport.send(encodeFrame(frame)),
      () => {
        this.sessions.delete(key);
      },
      args.options ?? {},
    );
    this.sessions.set(key, session);
    try {
      await session._initiateConnect();
    } catch (err) {
      this.sessions.delete(key);
      throw err;
    }
    return session;
  }

  async stop(): Promise<void> {
    for (const session of this.sessions.values()) {
      session._forceDisconnect();
    }
    this.sessions.clear();
    if (this.started) {
      await this.transport.stop();
      this.started = false;
    }
  }

  private onInboundFrame(bytes: Uint8Array): void {
    let frame: Ax25Frame;
    try {
      frame = decodeFrame(bytes);
    } catch {
      return; // malformed — drop.
    }
    // Route by (local=destination, peer=source).
    const local = frame.destination.callsign;
    const peer = frame.source.callsign;
    const key = sessionKey(local, peer);
    const session = this.sessions.get(key);
    if (session) {
      session._handleFrame(frame);
      return;
    }
    // No matching session. We don't currently auto-accept inbound SABMs;
    // that would require an "onConnectRequest" API which is out of scope.
    // Spec-correct behaviour would be to reply DM; we silently drop.
  }
}

function sessionKey(local: Callsign, peer: Callsign): string {
  return `${local.toString()}__${peer.toString()}`;
}
