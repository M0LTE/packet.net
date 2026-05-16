[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / Ax25SessionOptions

# Interface: Ax25SessionOptions

Defined in: [session.ts:34](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/session.ts#L34)

## Properties

| Property | Type | Description | Defined in |
| ------ | ------ | ------ | ------ |
| <a id="n2"></a> `n2?` | `number` | Maximum retries (N2). Default 10. | [session.ts:42](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/session.ts#L42) |
| <a id="pid"></a> `pid?` | `number` | PID for outbound I-frames. Default 0xF0 (no L3 protocol). | [session.ts:44](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/session.ts#L44) |
| <a id="t1ms"></a> `t1Ms?` | `number` | T1 retry timeout (ms). Default 3000. | [session.ts:36](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/session.ts#L36) |
| <a id="t2ms"></a> `t2Ms?` | `number` | T2 response-delay timeout (ms). Default 1500. | [session.ts:38](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/session.ts#L38) |
| <a id="t3ms"></a> `t3Ms?` | `number` | T3 inactive-link timeout (ms). Default 30000. | [session.ts:40](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/session.ts#L40) |
