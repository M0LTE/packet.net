[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / encodeKiss

# Function: encodeKiss()

```ts
function encodeKiss(
   port, 
   command, 
   payload): Uint8Array;
```

Defined in: [kiss.ts:45](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/kiss.ts#L45)

Encode a single KISS frame for transmission. The command byte
(`(port<<4)|cmd`) is itself escaped if it happens to collide with FEND/FESC,
to handle e.g. port=12 + Data=0xC -> 0xCC0 -> 0xC0 collision corner cases.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `port` | `number` |
| `command` | `number` |
| `payload` | `Uint8Array` |

## Returns

`Uint8Array`
