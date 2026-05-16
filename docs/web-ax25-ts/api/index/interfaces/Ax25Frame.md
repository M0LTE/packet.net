[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / Ax25Frame

# Interface: Ax25Frame

Defined in: [frame.ts:59](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L59)

One AX.25 frame as delivered by KISS — no opening / closing flag,
no FCS (the TNC handles HDLC framing and the FCS).

Layout per AX.25 v2.2 §3:
  [destination 7B] [source 7B] [digipeaters 0..8 × 7B] [control 1B]
  [pid 0..1B] [info 0..N B]

PID and info are present only on I and UI frames.

## Properties

| Property | Type | Description | Defined in |
| ------ | ------ | ------ | ------ |
| <a id="control"></a> `control` | `number` | Raw control byte. | [frame.ts:64](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L64) |
| <a id="destination"></a> `destination` | [`Ax25Address`](Ax25Address.md) | - | [frame.ts:60](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L60) |
| <a id="digipeaters"></a> `digipeaters` | readonly [`Ax25Address`](Ax25Address.md)[] | - | [frame.ts:62](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L62) |
| <a id="info"></a> `info` | `Uint8Array` | Information field. Always present (zero-length if absent). | [frame.ts:68](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L68) |
| <a id="pid"></a> `pid` | `number` \| `null` | PID byte, present on I/UI frames only. | [frame.ts:66](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L66) |
| <a id="source"></a> `source` | [`Ax25Address`](Ax25Address.md) | - | [frame.ts:61](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/frame.ts#L61) |
