[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / writeAddress

# Function: writeAddress()

```ts
function writeAddress(
   dest, 
   offset, 
   addr): void;
```

Defined in: [address.ts:59](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/address.ts#L59)

Encode this address slot into 7 bytes at `offset`.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `dest` | `Uint8Array` |
| `offset` | `number` |
| `addr` | [`Ax25Address`](../interfaces/Ax25Address.md) |

## Returns

`void`
