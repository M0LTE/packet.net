[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / iFrame

# Function: iFrame()

```ts
function iFrame(opts): Ax25Frame;
```

Defined in: [frame.ts:396](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L396)

Build an Information (I) frame. Always a command per AX.25 v2.2 §4.3.1.
Mod-8 control: `(N(R) << 5) | (P << 4) | (N(S) << 1) | 0`.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `opts` | [`FrameFactoryOpts`](../interfaces/FrameFactoryOpts.md) & \{ `info`: `Uint8Array`; `nr`: `number`; `ns`: `number`; `pid?`: `number`; `pollBit?`: `boolean`; \} |

## Returns

[`Ax25Frame`](../interfaces/Ax25Frame.md)
