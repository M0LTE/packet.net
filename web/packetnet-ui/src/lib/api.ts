// ============================================================
// pdn API client — the boundary the screens talk to.
//
// Two backends behind one typed surface:
//   - "mock" (default): resolves from lib/mock.ts; the monitor frame stream is
//     timer-driven. Lets every screen render + demo with no node.
//   - "live": real fetch against /api/v1 + an EventSource on /api/v1/events.
// Toggle with VITE_API_MODE=live (see vite proxy). The Slice-3 backend (locked
// in docs/node-api.yaml) lands behind the "live" path with no screen changes.
// ============================================================
import { useEffect, useRef, useState } from "react";
import type {
  NodeStatus, PortStatus, SessionInfo, NetRomRoutingSnapshot, NodeConfig,
  LinkStats, MonitorEvent, User, LogLine, ReconcileResult, ValidationProblem,
} from "./types";
import * as mock from "./mock";

const MODE: "mock" | "live" =
  (import.meta.env.VITE_API_MODE as "mock" | "live") ?? "mock";
const BASE = "/api/v1";

export const apiMode = MODE;

async function get<T>(path: string, mockValue: () => T): Promise<T> {
  if (MODE === "mock") {
    // tiny delay so loading states are exercised
    await new Promise((r) => setTimeout(r, 60));
    return structuredClone(mockValue());
  }
  const res = await fetch(`${BASE}${path}`, { headers: { accept: "application/json" } });
  if (!res.ok) throw new Error(`${path}: ${res.status} ${res.statusText}`);
  return (await res.json()) as T;
}

// A rejected config edit (HTTP 422) — carries the server's per-field validation
// problems so the editor can surface them inline.
export class ConfigRejected extends Error {
  constructor(public readonly problem: ValidationProblem) {
    super(problem.errors.map((e) => `${e.path}: ${e.message}`).join("; ") || "config rejected");
    this.name = "ConfigRejected";
  }
}

// A synthetic reconcile result for mock mode (no backend to ask). Marks everything
// "live" so the preview renders; real grouping comes from the server in live mode.
function mockReconcile(applied: boolean): ReconcileResult {
  return { valid: true, live: [{ path: "(mock)", impact: "live", summary: "Mock mode — no node to reconcile against." }], portRestart: [], nodeReset: [], applied };
}

// ---- read endpoints ----------------------------------------
export const api = {
  status: () => get<NodeStatus>("/status", () => mock.NODE_STATUS),
  ports: () => get<PortStatus[]>("/ports", () => Object.values(mock.PORT_STATUS)),
  sessions: () => get<SessionInfo[]>("/sessions", () => mock.SESSIONS),
  routes: () => get<NetRomRoutingSnapshot>("/netrom/routes", () => mock.NETROM),
  config: () => get<NodeConfig>("/config", () => mock.NODE_CONFIG),
  linkStats: () => get<LinkStats[]>("/links", () => mock.LINK_STATS),
  users: () => get<User[]>("/users", () => mock.USERS),
  log: () => get<LogLine[]>("/log", () => mock.LOG_TAIL),

  // ---- config write (Slice-3 step 2) ----
  // PUT the whole config; dryRun returns the reconcile preview without applying.
  // A 422 throws ConfigRejected carrying the validation problems.
  putConfig: (cfg: NodeConfig, opts: { dryRun?: boolean } = {}) =>
    writeConfig("/config", "PUT", JSON.stringify(cfg), "application/json", opts.dryRun ?? false),
  // The raw YAML the advanced editor round-trips.
  getConfigRaw: async (): Promise<string> => {
    if (MODE === "mock") return "# raw YAML round-trips against a live node\nschemaVersion: 1\n";
    const res = await fetch(`${BASE}/config/raw`, { headers: { accept: "text/plain" } });
    if (!res.ok) throw new Error(`/config/raw: ${res.status}`);
    return res.text();
  },
  putConfigRaw: (yaml: string, opts: { dryRun?: boolean } = {}) =>
    writeConfig("/config/raw", "PUT", yaml, "text/plain", opts.dryRun ?? false),
};

// Shared PUT helper for the config write endpoints: returns the ReconcileResult,
// or throws ConfigRejected on 422 (validation), Error on other failures.
async function writeConfig(
  path: string, method: string, body: string, contentType: string, dryRun: boolean,
): Promise<ReconcileResult> {
  if (MODE === "mock") {
    await new Promise((r) => setTimeout(r, 80));
    return mockReconcile(!dryRun);
  }
  const res = await fetch(`${BASE}${path}?dryRun=${dryRun}`, {
    method,
    headers: { "content-type": contentType, accept: "application/json" },
    body,
  });
  if (res.status === 422) {
    throw new ConfigRejected((await res.json()) as ValidationProblem);
  }
  if (!res.ok) throw new Error(`${path}: ${res.status} ${res.statusText}`);
  return (await res.json()) as ReconcileResult;
}

// ---- generic data hook -------------------------------------
export interface Query<T> { data: T | null; loading: boolean; error: string | null; reload: () => void }

export function useQuery<T>(fetcher: () => Promise<T>, deps: unknown[] = []): Query<T> {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  useEffect(() => {
    let live = true;
    setLoading(true);
    fetcher()
      .then((d) => { if (live) { setData(d); setError(null); } })
      .catch((e) => { if (live) setError(String(e?.message ?? e)); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tick, ...deps]);
  return { data, loading, error, reload: () => setTick((t) => t + 1) };
}

// ---- the live frame stream (SSE `frame` events) ------------
// onFrame is called per arriving MonitorEvent. Returns an unsubscribe.
export function subscribeFrames(onFrame: (f: MonitorEvent) => void): () => void {
  if (MODE === "mock") {
    const id = setInterval(() => {
      const burst = 1 + Math.floor(Math.random() * 2);
      for (let i = 0; i < burst; i++) onFrame(mock.makeFrame(new Date()));
    }, 700);
    return () => clearInterval(id);
  }
  const es = new EventSource(`${BASE}/events`);
  const handler = (e: MessageEvent) => {
    try { onFrame(JSON.parse(e.data) as MonitorEvent); } catch { /* ignore malformed */ }
  };
  es.addEventListener("frame", handler as EventListener);
  return () => { es.removeEventListener("frame", handler as EventListener); es.close(); };
}

/** Seed the monitor with a recent backlog (mock only; live seeds from the stream). */
export function seedFrames(n: number): MonitorEvent[] {
  return MODE === "mock" ? mock.seedFrames(n) : [];
}

// A small live frames-buffer hook for the monitor (ring buffer, newest first).
export function useFrameStream(cap = 500): {
  frames: MonitorEvent[];
  paused: boolean;
  setPaused: (p: boolean) => void;
  clear: () => void;
} {
  const [frames, setFrames] = useState<MonitorEvent[]>(() => seedFrames(40).reverse());
  const [paused, setPaused] = useState(false);
  const pausedRef = useRef(paused);
  pausedRef.current = paused;
  useEffect(() => {
    return subscribeFrames((f) => {
      if (pausedRef.current) return;
      setFrames((prev) => [f, ...prev].slice(0, cap));
    });
  }, [cap]);
  return { frames, paused, setPaused, clear: () => setFrames([]) };
}
