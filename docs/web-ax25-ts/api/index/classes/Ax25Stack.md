[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / Ax25Stack

# Class: Ax25Stack

Defined in: [session.ts:329](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L329)

Holds the transport, runs the inbound demux loop, and is the factory
for new [Ax25Session](Ax25Session.md)s.

## Constructors

### Constructor

```ts
new Ax25Stack(transport): Ax25Stack;
```

Defined in: [session.ts:334](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L334)

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `transport` | [`Ax25Transport`](../interfaces/Ax25Transport.md) |

#### Returns

`Ax25Stack`

## Methods

### connect()

```ts
connect(args): Promise<Ax25Session>;
```

Defined in: [session.ts:351](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L351)

Initiate a connected-mode session to `to`. Resolves with the session
once the SABM/UA handshake completes; rejects on N2 exhaustion or
peer DM.

`via` is the digipeater path; it's NOT supported in v1 and will throw.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `args` | \{ `from`: `string` \| [`Callsign`](Callsign.md); `options?`: [`Ax25SessionOptions`](../interfaces/Ax25SessionOptions.md); `to`: `string` \| [`Callsign`](Callsign.md); `via?`: `string`[]; \} |
| `args.from` | `string` \| [`Callsign`](Callsign.md) |
| `args.options?` | [`Ax25SessionOptions`](../interfaces/Ax25SessionOptions.md) |
| `args.to` | `string` \| [`Callsign`](Callsign.md) |
| `args.via?` | `string`[] |

#### Returns

`Promise`\<[`Ax25Session`](Ax25Session.md)\>

***

### start()

```ts
start(): Promise<void>;
```

Defined in: [session.ts:338](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L338)

#### Returns

`Promise`\<`void`\>

***

### stop()

```ts
stop(): Promise<void>;
```

Defined in: [session.ts:389](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L389)

#### Returns

`Promise`\<`void`\>
