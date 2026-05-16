[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / rnr

# Function: rnr()

```ts
function rnr(opts): Ax25Frame;
```

Defined in: [frame.ts:351](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L351)

Build a Receive Not Ready (RNR) supervisory frame.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `opts` | [`FrameFactoryOpts`](../interfaces/FrameFactoryOpts.md) & \{ `isCommand`: `boolean`; `nr`: `number`; `pollFinal?`: `boolean`; \} |

## Returns

[`Ax25Frame`](../interfaces/Ax25Frame.md)
