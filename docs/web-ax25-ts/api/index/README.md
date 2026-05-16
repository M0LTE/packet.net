[**@packet-net/ax25-ts**](../README.md)

***

[@packet-net/ax25-ts](../README.md) / index

# index

## Classes

| Class | Description |
| ------ | ------ |
| [Ax25Session](classes/Ax25Session.md) | One connected-mode AX.25 session. Created via [Ax25Stack.connect](classes/Ax25Stack.md#connect). Callers register data/disconnect listeners, push outbound bytes via [Ax25Session.write](classes/Ax25Session.md#write), and tear down the link via [Ax25Session.disconnect](classes/Ax25Session.md#disconnect). |
| [Ax25Stack](classes/Ax25Stack.md) | Holds the transport, runs the inbound demux loop, and is the factory for new [Ax25Session](classes/Ax25Session.md)s. |
| [Callsign](classes/Callsign.md) | AX.25 amateur-radio callsign with optional Secondary Station Identifier (SSID, 0-15). The base is 0-6 uppercase ASCII alphanumerics; `Parse` requires at least one character. |
| [KissDecoder](classes/KissDecoder.md) | Stateful KISS frame decoder. Push raw bytes from the serial port as they arrive; each `push` returns any complete frames extracted from the byte stream. The decoder retains in-progress frame state and escape mode across calls. |
| [WebSerialKissTransport](classes/WebSerialKissTransport.md) | KISS-over-Web-Serial transport. Wraps a `SerialPort` (or any stream-pair object with the same shape) and exposes a `start / send / stop` interface that surfaces AX.25 frame bytes to higher layers. |

## Interfaces

| Interface | Description |
| ------ | ------ |
| [Ax25Address](interfaces/Ax25Address.md) | One 7-octet address slot in an AX.25 frame header (AX.25 v2.2 §3.12). |
| [Ax25Frame](interfaces/Ax25Frame.md) | One AX.25 frame as delivered by KISS — no opening / closing flag, no FCS (the TNC handles HDLC framing and the FCS). |
| [Ax25SessionOptions](interfaces/Ax25SessionOptions.md) | - |
| [Ax25Transport](interfaces/Ax25Transport.md) | Transport-layer abstraction: send and receive raw AX.25 frame bytes (no KISS framing — that's the transport's job). |
| [FrameFactoryOpts](interfaces/FrameFactoryOpts.md) | - |
| [KissFrame](interfaces/KissFrame.md) | One decoded KISS frame. |
| [WebSerialKissTransportOptions](interfaces/WebSerialKissTransportOptions.md) | - |
| [WebSerialLikePort](interfaces/WebSerialLikePort.md) | Minimal duck-typed shape of a Web Serial port — keeps tests passing under Node where `SerialPort` isn't a global. In a browser, pass an actual `SerialPort` obtained from `navigator.serial.requestPort()`. |

## Type Aliases

| Type Alias | Description |
| ------ | ------ |
| [FrameKind](type-aliases/FrameKind.md) | The high-level frame kind, after classification of the control byte. Information mod-128 is NOT supported in v1 — only mod-8. |
| [KissCommand](type-aliases/KissCommand.md) | - |

## Variables

| Variable | Description |
| ------ | ------ |
| [ADDRESS\_ENCODED\_LENGTH](variables/ADDRESS_ENCODED_LENGTH.md) | Number of octets one address slot occupies on the wire. |
| [FEND](variables/FEND.md) | KISS TNC framing (SLIP-style escape sequences). |
| [FESC](variables/FESC.md) | - |
| [KISS\_CMD](variables/KISS_CMD.md) | KISS command codes (low nibble of the command byte). |
| [MAX\_DIGIPEATERS](variables/MAX_DIGIPEATERS.md) | Maximum digipeater chain length (§3.12.5). |
| [PID\_NET\_ROM](variables/PID_NET_ROM.md) | PID 0xCF — NET/ROM. |
| [PID\_NO\_LAYER\_3](variables/PID_NO_LAYER_3.md) | PID 0xF0 — no Layer 3 protocol implemented (AX.25 v2.2 §3.4). |
| [TFEND](variables/TFEND.md) | - |
| [TFESC](variables/TFESC.md) | - |

## Functions

| Function | Description |
| ------ | ------ |
| [classify](functions/classify.md) | Classify the control byte into a high-level frame kind. |
| [decodeFrame](functions/decodeFrame.md) | Decode an Ax25Frame from KISS-form bytes (no flag, no FCS). Throws on malformed input — call inside try/catch when feeding raw KISS payloads. |
| [disc](functions/disc.md) | Build a DISC command frame. |
| [dm](functions/dm.md) | Build a DM response frame. |
| [encodeFrame](functions/encodeFrame.md) | Encode an Ax25Frame into a flat Uint8Array (no KISS framing). |
| [encodeKiss](functions/encodeKiss.md) | Encode a single KISS frame for transmission. The command byte (`(port<<4)|cmd`) is itself escaped if it happens to collide with FEND/FESC, to handle e.g. port=12 + Data=0xC -> 0xCC0 -> 0xC0 collision corner cases. |
| [getNr](functions/getNr.md) | Mod-8 N(R) — valid on I frames and S frames. Bits 7-5 of the control byte. |
| [getNs](functions/getNs.md) | Mod-8 N(S) — only valid on I frames. Bits 3-1 of the control byte. |
| [iFrame](functions/iFrame.md) | Build an Information (I) frame. Always a command per AX.25 v2.2 §4.3.1. Mod-8 control: `(N(R) << 5) | (P << 4) | (N(S) << 1) | 0`. |
| [isCommand](functions/isCommand.md) | True if address C-bits encode a command per §6.1.2 (dest C=1, src C=0). |
| [isResponse](functions/isResponse.md) | True if address C-bits encode a response per §6.1.2 (dest C=0, src C=1). |
| [pollFinal](functions/pollFinal.md) | True if the P/F bit in the control byte is set. |
| [readAddress](functions/readAddress.md) | Read one 7-octet slot starting at `offset`. |
| [rej](functions/rej.md) | Build a REJ supervisory frame. |
| [requiredBytes](functions/requiredBytes.md) | Compute total wire length the encoder will produce for this frame. |
| [rnr](functions/rnr.md) | Build a Receive Not Ready (RNR) supervisory frame. |
| [rr](functions/rr.md) | Build a Receive Ready (RR) supervisory frame. |
| [sabm](functions/sabm.md) | Build a SABM (Set Async Balanced Mode, mod-8) command frame. |
| [ua](functions/ua.md) | Build a UA response frame. |
| [ui](functions/ui.md) | Build a UI frame. Command/response per `isCommand`. |
| [writeAddress](functions/writeAddress.md) | Encode this address slot into 7 bytes at `offset`. |
