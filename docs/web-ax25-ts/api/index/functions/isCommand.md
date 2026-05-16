[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / isCommand

# Function: isCommand()

```ts
function isCommand(frame): boolean;
```

Defined in: [frame.ts:72](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L72)

True if address C-bits encode a command per §6.1.2 (dest C=1, src C=0).

## Parameters

| Parameter | Type |
| ------ | ------ |
| `frame` | [`Ax25Frame`](../interfaces/Ax25Frame.md) |

## Returns

`boolean`
