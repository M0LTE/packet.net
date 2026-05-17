[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / Ax25Listener

# Class: Ax25Listener

Defined in: listener.ts:168

First-class AX.25 inbound-acceptance coordinator. Owns one
[Ax25Transport](../interfaces/Ax25Transport.md), address-filters inbound frames against
[Ax25Listener.myCall](#mycall), dispatches to the per-peer [Ax25ListenerSession](Ax25ListenerSession.md)
(creating one on first contact — inbound SABM or outbound
[Ax25Listener.connect](#connect)), and surfaces per-frame TX/RX events so
monitor / promiscuous-capture UIs can tap the channel.

Sibling to [Ax25Stack](Ax25Stack.md) — `Ax25Stack` is the outbound-only
convenience facade existing consumers use; `Ax25Listener` is the
inbound-accepting node-shape for BBSes, gateways, and the like.

Mirrors `Packet.Ax25.Session.Ax25Listener` from the C# runtime; the
three carried-over bug fixes (handler-exception isolation, via-chain
reversal, cache-miss DM) are applied here too — see the PR description
for the cross-references.

## Constructors

### Constructor

```ts
new Ax25Listener(transport, options): Ax25Listener;
```

Defined in: listener.ts:189

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `transport` | [`Ax25Transport`](../interfaces/Ax25Transport.md) |
| `options` | [`Ax25ListenerOptions`](../interfaces/Ax25ListenerOptions.md) |

#### Returns

`Ax25Listener`

## Properties

| Property | Modifier | Type | Defined in |
| ------ | ------ | ------ | ------ |
| <a id="mycall"></a> `myCall` | `readonly` | [`Callsign`](Callsign.md) | listener.ts:169 |

## Accessors

### acceptIncoming

#### Get Signature

```ts
get acceptIncoming(): boolean;
```

Defined in: listener.ts:222

Whether the listener will build a session for inbound SABMs. Flip to
`false` to reject all new incoming (figc4.1 t15 → DM); existing
sessions keep running. Default `true`.

##### Returns

`boolean`

#### Set Signature

```ts
set acceptIncoming(value): void;
```

Defined in: listener.ts:225

##### Parameters

| Parameter | Type |
| ------ | ------ |
| `value` | `boolean` |

##### Returns

`void`

***

### isRunning

#### Get Signature

```ts
get isRunning(): boolean;
```

Defined in: listener.ts:213

True once [start](#start) has been called and the inbound pump is running.

##### Returns

`boolean`

## Methods

### connect()

```ts
connect(remote): Promise<Ax25ListenerSession>;
```

Defined in: listener.ts:269

Initiate an outbound connect against this listener's
[myCall](#mycall) + the given remote. Reuses the cached session for
that peer if one exists (preserves SRT / T1V history); otherwise
builds one. Resolves once DL-CONNECT-confirm arrives.

Rejects with `Error` if the SDL responds with DM (peer refused)
or torn down before the connect completed; rejects with a timeout
error if N2 × T1V elapses with no UA.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `remote` | `string` \| [`Callsign`](Callsign.md) |

#### Returns

`Promise`\<[`Ax25ListenerSession`](Ax25ListenerSession.md)\>

***

### dispose()

```ts
dispose(): Promise<void>;
```

Defined in: listener.ts:355

Dispose the listener: stop the pump + clear the per-peer cache.

#### Returns

`Promise`\<`void`\>

***

### offFrameTraced()

```ts
offFrameTraced(callback): void;
```

Defined in: listener.ts:243

Unregister a previously-registered frame-traced callback.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `callback` | (`event`) => `void` |

#### Returns

`void`

***

### offSessionAccepted()

```ts
offSessionAccepted(callback): void;
```

Defined in: listener.ts:234

Unregister a previously-registered session-accepted callback.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `callback` | (`session`) => `void` |

#### Returns

`void`

***

### onFrameTraced()

```ts
onFrameTraced(callback): void;
```

Defined in: listener.ts:239

Register a callback for every TX/RX frame the listener observes.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `callback` | (`event`) => `void` |

#### Returns

`void`

***

### onSessionAccepted()

```ts
onSessionAccepted(callback): void;
```

Defined in: listener.ts:230

Register a callback for new (or re-confirmed) sessions.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `callback` | (`session`) => `void` |

#### Returns

`void`

***

### start()

```ts
start(): Promise<void>;
```

Defined in: listener.ts:252

Spin up the inbound pump. Returns once the transport's
`start` has resolved; the pump itself continues running in the
background until [stop](#stop).

#### Returns

`Promise`\<`void`\>

***

### stop()

```ts
stop(): Promise<void>;
```

Defined in: listener.ts:337

Stop the inbound pump and release the transport.

#### Returns

`Promise`\<`void`\>
