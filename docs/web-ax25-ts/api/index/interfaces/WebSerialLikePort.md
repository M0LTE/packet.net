[**@packet-net/ax25-ts**](../../README.md)

***

[@packet-net/ax25-ts](../../README.md) / [index](../README.md) / WebSerialLikePort

# Interface: WebSerialLikePort

Defined in: [webserial-transport.ts:9](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/webserial-transport.ts#L9)

Minimal duck-typed shape of a Web Serial port — keeps tests passing
under Node where `SerialPort` isn't a global. In a browser, pass an
actual `SerialPort` obtained from `navigator.serial.requestPort()`.

## Properties

| Property | Type | Defined in |
| ------ | ------ | ------ |
| <a id="readable"></a> `readable` | `ReadableStream`\<`Uint8Array`\<`ArrayBufferLike`\>\> \| `null` | [webserial-transport.ts:12](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/webserial-transport.ts#L12) |
| <a id="writable"></a> `writable` | `WritableStream`\<`Uint8Array`\<`ArrayBufferLike`\>\> \| `null` | [webserial-transport.ts:13](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/webserial-transport.ts#L13) |

## Methods

### close()

```ts
close(): Promise<void>;
```

Defined in: [webserial-transport.ts:11](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/webserial-transport.ts#L11)

#### Returns

`Promise`\<`void`\>

***

### open()

```ts
open(options): Promise<void>;
```

Defined in: [webserial-transport.ts:10](https://github.com/M0LTE/packet.net/blob/main/web/ax25-ts/src/webserial-transport.ts#L10)

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `options` | \{ `baudRate`: `number`; \} |
| `options.baudRate` | `number` |

#### Returns

`Promise`\<`void`\>
