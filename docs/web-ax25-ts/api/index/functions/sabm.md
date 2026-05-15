[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / sabm

# Function: sabm()

```ts
function sabm(opts): Ax25Frame;
```

Defined in: [frame.ts:260](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L260)

Build a SABM (Set Async Balanced Mode, mod-8) command frame.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `opts` | [`FrameFactoryOpts`](../interfaces/FrameFactoryOpts.md) & \{ `pollBit?`: `boolean`; \} |

## Returns

[`Ax25Frame`](../interfaces/Ax25Frame.md)
