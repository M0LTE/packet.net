[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / getNs

# Function: getNs()

```ts
function getNs(frame): number;
```

Defined in: [frame.ts:122](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/frame.ts#L122)

Mod-8 N(S) — only valid on I frames. Bits 3-1 of the control byte.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `frame` | [`Ax25Frame`](../interfaces/Ax25Frame.md) |

## Returns

`number`
