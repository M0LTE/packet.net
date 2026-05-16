[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / FrameKind

# Type Alias: FrameKind

```ts
type FrameKind = "SABM" | "DISC" | "UA" | "DM" | "UI" | "RR" | "RNR" | "REJ" | "I" | "UNKNOWN";
```

Defined in: [frame.ts:37](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L37)

The high-level frame kind, after classification of the control byte.
Information mod-128 is NOT supported in v1 — only mod-8.
