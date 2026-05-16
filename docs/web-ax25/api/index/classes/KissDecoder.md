[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / KissDecoder

# Class: KissDecoder

Defined in: [kiss.ts:88](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/kiss.ts#L88)

Stateful KISS frame decoder. Push raw bytes from the serial port as they
arrive; each `push` returns any complete frames extracted from the byte
stream. The decoder retains in-progress frame state and escape mode across
calls.

## Constructors

### Constructor

```ts
new KissDecoder(): KissDecoder;
```

#### Returns

`KissDecoder`

## Methods

### push()

```ts
push(bytes): KissFrame[];
```

Defined in: [kiss.ts:93](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/kiss.ts#L93)

Push a chunk of received bytes. Returns 0+ decoded frames.

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `bytes` | `Uint8Array` |

#### Returns

[`KissFrame`](../interfaces/KissFrame.md)[]

***

### reset()

```ts
reset(): void;
```

Defined in: [kiss.ts:120](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/kiss.ts#L120)

#### Returns

`void`
