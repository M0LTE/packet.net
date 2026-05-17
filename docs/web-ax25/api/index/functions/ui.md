[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / ui

# Function: ui()

```ts
function ui(opts): Ax25Frame;
```

Defined in: [frame.ts:335](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/frame.ts#L335)

Build a UI frame. Command/response per `isCommand`.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `opts` | [`FrameFactoryOpts`](../interfaces/FrameFactoryOpts.md) & \{ `info`: `Uint8Array`; `isCommand?`: `boolean`; `pid?`: `number`; `pollFinal?`: `boolean`; \} |

## Returns

[`Ax25Frame`](../interfaces/Ax25Frame.md)
