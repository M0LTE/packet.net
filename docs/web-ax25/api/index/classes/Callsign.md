[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / Callsign

# Class: Callsign

Defined in: [callsign.ts:8](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/callsign.ts#L8)

AX.25 amateur-radio callsign with optional Secondary Station Identifier
(SSID, 0-15). The base is 0-6 uppercase ASCII alphanumerics; `Parse`
requires at least one character.

Mirrors `Packet.Core.Callsign` on the C# side.

## Constructors

### Constructor

```ts
new Callsign(base, ssid?): Callsign;
```

Defined in: [callsign.ts:16](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/callsign.ts#L16)

#### Parameters

| Parameter | Type | Default value |
| ------ | ------ | ------ |
| `base` | `string` | `undefined` |
| `ssid` | `number` | `0` |

#### Returns

`Callsign`

## Properties

| Property | Modifier | Type | Description | Defined in |
| ------ | ------ | ------ | ------ | ------ |
| <a id="base"></a> `base` | `readonly` | `string` | Uppercase A-Z / 0-9, length 0-6. Empty is permitted (BPQ ID-beacon style) when constructed from wire bytes, but [Callsign.parse](#parse) treats empty text as an error. | [callsign.ts:12](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/callsign.ts#L12) |
| <a id="ssid"></a> `ssid` | `readonly` | `number` | Secondary station identifier, 0-15. | [callsign.ts:14](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/callsign.ts#L14) |

## Methods

### equals()

```ts
equals(other): boolean;
```

Defined in: [callsign.ts:68](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/callsign.ts#L68)

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `other` | `Callsign` |

#### Returns

`boolean`

***

### parse()

```ts
static parse(text): Callsign;
```

Defined in: [callsign.ts:33](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/callsign.ts#L33)

Parse the canonical text form: "BASE" or "BASE-SSID".

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `text` | `string` |

#### Returns

`Callsign`

***

### toString()

```ts
toString(): string;
```

Defined in: [callsign.ts:64](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/callsign.ts#L64)

#### Returns

`string`

***

### tryParse()

```ts
static tryParse(text): Callsign | null;
```

Defined in: [callsign.ts:41](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/callsign.ts#L41)

#### Parameters

| Parameter | Type |
| ------ | ------ |
| `text` | `string` \| `null` \| `undefined` |

#### Returns

`Callsign` \| `null`
