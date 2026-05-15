[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / readAddress

# Function: readAddress()

```ts
function readAddress(bytes, offset): Ax25Address;
```

Defined in: [address.ts:27](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/address.ts#L27)

Read one 7-octet slot starting at `offset`.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `bytes` | `Uint8Array` |
| `offset` | `number` |

## Returns

[`Ax25Address`](../interfaces/Ax25Address.md)
