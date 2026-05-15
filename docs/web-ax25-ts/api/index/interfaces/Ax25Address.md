[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / Ax25Address

# Interface: Ax25Address

Defined in: [address.ts:15](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/address.ts#L15)

One 7-octet address slot in an AX.25 frame header (AX.25 v2.2 §3.12).

Octets 1-6: callsign chars, each left-shifted by 1.
Octet 7 (SSID byte): C/H | R | R | SSID(4) | E
  - C/H: command/response on destination & source slots; has-been-repeated on digi slots.
  - R: reserved bits — v2.2 default is "11".
  - SSID: 4-bit station identifier.
  - E: end-of-address — set on the LAST slot of the whole address field.

Mirrors `Packet.Core.Ax25Address` on the C# side.

## Properties

| Property | Type | Description | Defined in |
| ------ | ------ | ------ | ------ |
| <a id="callsign"></a> `callsign` | [`Callsign`](../classes/Callsign.md) | - | [address.ts:16](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/address.ts#L16) |
| <a id="crhbit"></a> `crhBit` | `boolean` | C-bit (destination/source) or H-bit (repeater). | [address.ts:18](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/address.ts#L18) |
| <a id="extensionbit"></a> `extensionBit` | `boolean` | End-of-address (set on the last slot only). | [address.ts:20](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/address.ts#L20) |
