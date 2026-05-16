[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [tcp-transport](../README.md) / TcpKissTransport

# Class: TcpKissTransport

Defined in: [tcp-transport.ts:46](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/tcp-transport.ts#L46)

KISS-over-TCP transport for Node. Open a socket on `start`, push inbound
bytes through a `KissDecoder` and surface AX.25 frame payloads via the
`onFrame` callback; `send` KISS-encodes outbound frames and writes
them; `stop` closes the socket cleanly.

## Implements

- [`Ax25Transport`](../../index/interfaces/Ax25Transport.md)

## Constructors

### Constructor

```ts
new TcpKissTransport(
   host, 
   port, 
   opts?): TcpKissTransport;
```

Defined in: [tcp-transport.ts:57](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/tcp-transport.ts#L57)

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `host` | `string` |
| `port` | `number` |
| `opts` | [`TcpKissTransportOptions`](../interfaces/TcpKissTransportOptions.md) |

#### Returns

`TcpKissTransport`

## Methods

### send()

```ts
send(axBytes): Promise<void>;
```

Defined in: [tcp-transport.ts:130](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/tcp-transport.ts#L130)

Send one AX.25 frame to the modem.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `axBytes` | `Uint8Array` |

#### Returns

`Promise`\<`void`\>

#### Implementation of

[`Ax25Transport`](../../index/interfaces/Ax25Transport.md).[`send`](../../index/interfaces/Ax25Transport.md#send)

***

### start()

```ts
start(onFrame): Promise<void>;
```

Defined in: [tcp-transport.ts:66](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/tcp-transport.ts#L66)

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

[`Ax25Transport`](../../index/interfaces/Ax25Transport.md).[`start`](../../index/interfaces/Ax25Transport.md#start)

***

### stop()

```ts
stop(): Promise<void>;
```

Defined in: [tcp-transport.ts:143](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/tcp-transport.ts#L143)

Stop the transport and release resources.

#### Returns

`Promise`\<`void`\>

#### Implementation of

[`Ax25Transport`](../../index/interfaces/Ax25Transport.md).[`stop`](../../index/interfaces/Ax25Transport.md#stop)
