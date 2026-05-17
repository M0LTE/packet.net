[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / Ax25ListenerSession

# Class: Ax25ListenerSession

Defined in: listener.ts:91

One AX.25 session managed by a listener — built on top of the SDL
session driver, identical in shape to a session inside `Ax25Stack`
(except the listener owns the inbound pump rather than the
outbound-only `connect()` factory).

Listener-built sessions don't have an `_initiateConnect` / `_handleFrame`
surface — the listener feeds events directly via [postEvent](#postevent).
Public surface for consumers:

  - [state](#state), [context](#context) — read-only inspection
  - [postEvent](#postevent) — push DL primitives (DL_CONNECT_request,
    DL_DISCONNECT_request, DL_DATA_request) at the session
  - [onDataLinkSignal](#ondatalinksignal) — subscribe to upward signals
    (DL_CONNECT_confirm, DL_DATA_indication, DL_DISCONNECT_indication,
    DL_ERROR_indication, …) emitted by the SDL action chain
  - [offDataLinkSignal](#offdatalinksignal) — unsubscribe

## Properties

| Property | Modifier | Type | Defined in |
| ------ | ------ | ------ | ------ |
| <a id="context"></a> `context` | `readonly` | `Ax25SessionContext` | listener.ts:92 |

## Accessors

### state

#### Get Signature

```ts
get state(): string;
```

Defined in: listener.ts:103

Current SDL state name (e.g. "Disconnected", "Connected").

##### Returns

`string`

## Methods

### offDataLinkSignal()

```ts
offDataLinkSignal(callback): void;
```

Defined in: listener.ts:118

Unsubscribe a previously-registered signal listener.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `callback` | (`signal`) => `void` |

#### Returns

`void`

***

### onDataLinkSignal()

```ts
onDataLinkSignal(callback): void;
```

Defined in: listener.ts:113

Subscribe to upward signals from the SDL action chain.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `callback` | (`signal`) => `void` |

#### Returns

`void`

***

### postEvent()

```ts
postEvent(event): void;
```

Defined in: listener.ts:108

Drive one upper-layer / frame event through the SDL state machine.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `event` | `Ax25Event` |

#### Returns

`void`
