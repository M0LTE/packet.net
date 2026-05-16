[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / isResponse

# Function: isResponse()

```ts
function isResponse(frame): boolean;
```

Defined in: [frame.ts:77](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/frame.ts#L77)

True if address C-bits encode a response per §6.1.2 (dest C=0, src C=1).

## Parameters

| Parameter | Type |
| ------ | ------ |
| `frame` | [`Ax25Frame`](../interfaces/Ax25Frame.md) |

## Returns

`boolean`
