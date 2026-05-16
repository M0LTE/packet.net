[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / Ax25Session

# Class: Ax25Session

Defined in: [session.ts:53](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L53)

One connected-mode AX.25 session. Created via [Ax25Stack.connect](Ax25Stack.md#connect).
Callers register data/disconnect listeners, push outbound bytes via
[Ax25Session.write](#write), and tear down the link via
[Ax25Session.disconnect](#disconnect).

## Properties

| Property | Modifier | Type | Defined in |
| ------ | ------ | ------ | ------ |
| <a id="from"></a> `from` | `readonly` | [`Callsign`](Callsign.md) | [session.ts:54](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L54) |
| <a id="to"></a> `to` | `readonly` | [`Callsign`](Callsign.md) | [session.ts:55](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L55) |

## Methods

### disconnect()

```ts
disconnect(): Promise<void>;
```

Defined in: [session.ts:157](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L157)

Initiate disconnect. Resolves when the link is fully torn down.

#### Returns

`Promise`\<`void`\>

***

### onData()

```ts
onData(callback): void;
```

Defined in: [session.ts:130](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L130)

Register a callback invoked when the peer delivers I-frame info.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `callback` | (`chunk`) => `void` |

#### Returns

`void`

***

### onDisconnected()

```ts
onDisconnected(callback): void;
```

Defined in: [session.ts:135](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L135)

Register a callback invoked when the session enters Disconnected.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `callback` | () => `void` |

#### Returns

`void`

***

### write()

```ts
write(chunk): Promise<void>;
```

Defined in: [session.ts:144](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/session.ts#L144)

Queue a payload for transmission. Resolves once the bytes are
accepted into the local TX queue (not once the peer has ack'd —
that would require a much richer API).

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `chunk` | `Uint8Array` |

#### Returns

`Promise`\<`void`\>
