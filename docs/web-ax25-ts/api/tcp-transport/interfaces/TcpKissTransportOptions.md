[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [tcp-transport](../README.md) / TcpKissTransportOptions

# Interface: TcpKissTransportOptions

Defined in: [tcp-transport.ts:24](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/tcp-transport.ts#L24)

## Properties

| Property | Type | Description | Defined in |
| ------ | ------ | ------ | ------ |
| <a id="connecttimeoutms"></a> `connectTimeoutMs?` | `number` | Optional connect timeout in ms. Default 5000. | [tcp-transport.ts:28](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/tcp-transport.ts#L28) |
| <a id="kissport"></a> `kissPort?` | `number` | Multi-drop KISS port number (0-15). Default 0. | [tcp-transport.ts:26](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/tcp-transport.ts#L26) |
| <a id="socketfactory"></a> `socketFactory?` | (`host`, `port`) => `Socket` | Socket factory hook (test seam). When provided, `start()` calls this instead of `net.createConnection`. Production callers leave it undefined; the unit tests pass a `MockSocket` to exercise the read/write/close paths without dialing real TCP. | [tcp-transport.ts:35](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/tcp-transport.ts#L35) |
