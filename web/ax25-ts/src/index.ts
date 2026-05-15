/**
 * @packet-net/ax25-ts — browser-targeted TypeScript library for AX.25 v2.2
 * connected-mode sessions over Web Serial KISS modems.
 *
 * Quick start (see README for the long-form):
 *
 * ```ts
 * import {
 *   Ax25Stack,
 *   WebSerialKissTransport,
 *   Callsign,
 * } from "@packet-net/ax25-ts";
 *
 * const port = await navigator.serial.requestPort();
 * const transport = new WebSerialKissTransport(port, { baudRate: 9600 });
 * const stack = new Ax25Stack(transport);
 * await stack.start();
 * const session = await stack.connect({ from: "M0LTE-2", to: "GB7CIP" });
 * session.onData((chunk) => console.log(new TextDecoder().decode(chunk)));
 * await session.write(new TextEncoder().encode("hello\r"));
 * // ...
 * await session.disconnect();
 * await stack.stop();
 * ```
 *
 * Scope summary — what IS and ISN'T implemented:
 *
 *   ✓ Frame codec for SABM, DISC, UA, DM, UI, RR, RNR, REJ, I (mod-8)
 *   ✓ 7-octet callsign codec with SSID + C/H + E bit handling
 *   ✓ KISS framing (FEND/FESC escape, port nibble)
 *   ✓ Web Serial transport (Chrome / Edge / Opera on a desktop with the
 *     `chrome://flags/#enable-experimental-web-platform-features` setting,
 *     or unflagged in supported browsers)
 *   ✓ SABM → UA → Connected, DISC → UA → Disconnected
 *   ✓ I-frame TX/RX with V(s)/V(r)/V(a) bookkeeping, k=1 window
 *   ✓ T1 retry (default 3 s), capped at N2 (default 10)
 *
 *   ✗ mod-128 (SABME, extended sequence numbers)
 *   ✗ REJ recovery (we ack-advance on REJ and rely on T1 retransmit)
 *   ✗ SREJ
 *   ✗ T1 dynamic adjustment (fixed 3-second default)
 *   ✗ FRMR generation/handling
 *   ✗ Multi-frame TX windowing (k>1)
 *   ✗ figc4.7 subroutine framework (Establish_Data_Link, Select_T1_Value, …)
 *   ✗ Digipeater paths (`via` throws "not implemented")
 *   ✗ TCP/AGW/audio transports — Web Serial only
 *   ✗ Inbound connection acceptance (no `onConnectRequest` API)
 */

export { Callsign } from "./callsign.js";
export {
  ADDRESS_ENCODED_LENGTH,
  type Ax25Address,
  readAddress,
  writeAddress,
} from "./address.js";
export {
  type Ax25Frame,
  type FrameFactoryOpts,
  type FrameKind,
  MAX_DIGIPEATERS,
  PID_NET_ROM,
  PID_NO_LAYER_3,
  classify,
  decodeFrame,
  disc,
  dm,
  encodeFrame,
  getNr,
  getNs,
  iFrame,
  isCommand,
  isResponse,
  pollFinal,
  requiredBytes,
  rej,
  rnr,
  rr,
  sabm,
  ua,
  ui,
} from "./frame.js";
export {
  FEND,
  FESC,
  KISS_CMD,
  type KissCommand,
  KissDecoder,
  type KissFrame,
  TFEND,
  TFESC,
  encodeKiss,
} from "./kiss.js";
export type { Ax25Transport } from "./transport.js";
export {
  WebSerialKissTransport,
  type WebSerialKissTransportOptions,
  type WebSerialLikePort,
} from "./webserial-transport.js";
export {
  Ax25Session,
  type Ax25SessionOptions,
  Ax25Stack,
} from "./session.js";
