[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / decodeFrame

# Function: decodeFrame()

```ts
function decodeFrame(bytes): Ax25Frame;
```

Defined in: [frame.ts:173](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/frame.ts#L173)

Decode an Ax25Frame from KISS-form bytes (no flag, no FCS). Throws on
malformed input — call inside try/catch when feeding raw KISS payloads.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `bytes` | `Uint8Array` |

## Returns

[`Ax25Frame`](../interfaces/Ax25Frame.md)
