[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / Ax25Transport

# Interface: Ax25Transport

Defined in: [transport.ts:14](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/transport.ts#L14)

Transport-layer abstraction: send and receive raw AX.25 frame bytes
(no KISS framing — that's the transport's job).

Concrete implementations:
  - [WebSerialKissTransport](../classes/WebSerialKissTransport.md) — KISS over Web Serial (browser).
  - `TcpKissTransport` (Node-only, via `@packet-net/ax25-ts/tcp-transport` subpath import).
  - `MockTransport` (tests/mock-transport.ts) — paired in-memory mock for tests.

Future implementations (out of scope for v0.1):
  - AgwTransport
  - AxudpTransport

## Methods

### send()

```ts
send(axBytes): Promise<void>;
```

Defined in: [transport.ts:23](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/transport.ts#L23)

Send one AX.25 frame to the modem.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `axBytes` | `Uint8Array` |

#### Returns

`Promise`\<`void`\>

***

### start()

```ts
start(onFrame): Promise<void>;
```

Defined in: [transport.ts:20](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/transport.ts#L20)

Start the transport and subscribe to inbound AX.25 frames. The
callback receives KISS-stripped AX.25 frame bytes (no FCS, no FEND).
Idempotent: calling `start` while running re-binds the callback.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `onFrame` | (`axBytes`) => `void` |

#### Returns

`Promise`\<`void`\>

***

### stop()

```ts
stop(): Promise<void>;
```

Defined in: [transport.ts:26](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/transport.ts#L26)

Stop the transport and release resources.

#### Returns

`Promise`\<`void`\>
