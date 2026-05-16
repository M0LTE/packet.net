[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / rej

# Function: rej()

```ts
function rej(opts): Ax25Frame;
```

Defined in: [frame.ts:372](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L372)

Build a REJ supervisory frame.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `opts` | [`FrameFactoryOpts`](../interfaces/FrameFactoryOpts.md) & \{ `isCommand`: `boolean`; `nr`: `number`; `pollFinal?`: `boolean`; \} |

## Returns

[`Ax25Frame`](../interfaces/Ax25Frame.md)
