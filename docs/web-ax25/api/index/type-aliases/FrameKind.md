[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / FrameKind

# Type Alias: FrameKind

```ts
type FrameKind = 
  | "SABM"
  | "SABME"
  | "DISC"
  | "UA"
  | "DM"
  | "UI"
  | "RR"
  | "RNR"
  | "REJ"
  | "I"
  | "UNKNOWN";
```

Defined in: [frame.ts:38](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/frame.ts#L38)

The high-level frame kind, after classification of the control byte.
Information mod-128 is NOT supported in v1 — only mod-8.
