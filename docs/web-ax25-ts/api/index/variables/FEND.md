[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / FEND

# Variable: FEND

```ts
const FEND: 192 = 0xc0;
```

Defined in: [kiss.ts:11](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/kiss.ts#L11)

KISS TNC framing (SLIP-style escape sequences).

See https://github.com/packethacking/ax25spec/blob/main/doc/kiss-tnc-protocol.md

Wire layout: `FEND | (port<<4)|cmd | (escaped payload) | FEND`
  - FEND (0xC0) inside the payload escapes to FESC TFEND.
  - FESC (0xDB) inside the payload escapes to FESC TFESC.
