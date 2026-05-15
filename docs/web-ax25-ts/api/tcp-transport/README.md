[**@packet-net/ax25-ts**](../README.md)

***

[@packet-net/ax25-ts](../README.md) / tcp-transport

# tcp-transport

## Classes

| Class | Description |
| ------ | ------ |
| [TcpKissTransport](classes/TcpKissTransport.md) | KISS-over-TCP transport for Node. Open a socket on `start`, push inbound bytes through a `KissDecoder` and surface AX.25 frame payloads via the `onFrame` callback; `send` KISS-encodes outbound frames and writes them; `stop` closes the socket cleanly. |

## Interfaces

| Interface | Description |
| ------ | ------ |
| [TcpKissTransportOptions](interfaces/TcpKissTransportOptions.md) | - |
