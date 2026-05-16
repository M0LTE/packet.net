[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / KissFrame

# Interface: KissFrame

Defined in: [kiss.ts:31](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/kiss.ts#L31)

One decoded KISS frame.

## Properties

| Property | Type | Description | Defined in |
| ------ | ------ | ------ | ------ |
| <a id="command"></a> `command` | `number` | KISS command code (low nibble). | [kiss.ts:35](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/kiss.ts#L35) |
| <a id="payload"></a> `payload` | `Uint8Array` | Payload, unescaped. | [kiss.ts:37](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/kiss.ts#L37) |
| <a id="port"></a> `port` | `number` | Multi-drop port (0-15). | [kiss.ts:33](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/kiss.ts#L33) |
