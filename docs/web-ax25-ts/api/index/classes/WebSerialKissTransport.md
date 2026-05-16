[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / WebSerialKissTransport

# Class: WebSerialKissTransport

Defined in: [webserial-transport.ts:31](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/webserial-transport.ts#L31)

KISS-over-Web-Serial transport. Wraps a `SerialPort` (or any
stream-pair object with the same shape) and exposes a `start / send /
stop` interface that surfaces AX.25 frame bytes to higher layers.

The transport opens the port on `start`, attaches reader+writer streams,
runs a read loop on a background task, and closes everything on `stop`.

## Implements

- [`Ax25Transport`](../interfaces/Ax25Transport.md)

## Constructors

### Constructor

```ts
new WebSerialKissTransport(port, opts?): WebSerialKissTransport;
```

Defined in: [webserial-transport.ts:42](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/webserial-transport.ts#L42)

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `port` | [`WebSerialLikePort`](../interfaces/WebSerialLikePort.md) |
| `opts` | [`WebSerialKissTransportOptions`](../interfaces/WebSerialKissTransportOptions.md) |

#### Returns

`WebSerialKissTransport`

## Methods

### send()

```ts
send(axBytes): Promise<void>;
```

Defined in: [webserial-transport.ts:61](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/webserial-transport.ts#L61)

Send one AX.25 frame to the modem.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `axBytes` | `Uint8Array` |

#### Returns

`Promise`\<`void`\>

#### Implementation of

[`Ax25Transport`](../interfaces/Ax25Transport.md).[`send`](../interfaces/Ax25Transport.md#send)

***

### start()

```ts
start(onFrame): Promise<void>;
```

Defined in: [webserial-transport.ts:48](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/webserial-transport.ts#L48)

Start the transport and subscribe to inbound AX.25 frames. The
callback receives KISS-stripped AX.25 frame bytes (no FCS, no FEND).
Idempotent: calling `start` while running re-binds the callback.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `onFrame` | (`axBytes`) => `void` |

#### Returns

`Promise`\<`void`\>

#### Implementation of

[`Ax25Transport`](../interfaces/Ax25Transport.md).[`start`](../interfaces/Ax25Transport.md#start)

***

### stop()

```ts
stop(): Promise<void>;
```

Defined in: [webserial-transport.ts:67](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/webserial-transport.ts#L67)

Stop the transport and release resources.

#### Returns

`Promise`\<`void`\>

#### Implementation of

[`Ax25Transport`](../interfaces/Ax25Transport.md).[`stop`](../interfaces/Ax25Transport.md#stop)
