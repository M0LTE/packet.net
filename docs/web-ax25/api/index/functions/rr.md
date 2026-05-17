[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / rr

# Function: rr()

```ts
function rr(opts): Ax25Frame;
```

Defined in: [frame.ts:355](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/frame.ts#L355)

Build a Receive Ready (RR) supervisory frame.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `opts` | [`FrameFactoryOpts`](../interfaces/FrameFactoryOpts.md) & \{ `isCommand`: `boolean`; `nr`: `number`; `pollFinal?`: `boolean`; \} |

## Returns

[`Ax25Frame`](../interfaces/Ax25Frame.md)
