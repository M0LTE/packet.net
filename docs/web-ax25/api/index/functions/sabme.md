[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / sabme

# Function: sabme()

```ts
function sabme(opts): Ax25Frame;
```

Defined in: [frame.ts:285](https://github.com/M0LTE/packet.net/blob/main/web/ax25/src/frame.ts#L285)

Build a SABME (Set Async Balanced Mode Extended, mod-128) command frame.

The TS runtime doesn't drive mod-128 sequence numbers end-to-end — the
SDL's `version_2_2` predicate plus mod-128 sequence machinery are
gated behind the `isExtended` context flag, which the runtime
doesn't flip on its own. The factory + classify branch are wired
so [Ax25Listener](../classes/Ax25Listener.md) tests can inject a SABME-shaped frame and
the SDL's `SABME_received` event arm fires.

## Parameters

| Parameter | Type |
| ------ | ------ |
| `opts` | [`FrameFactoryOpts`](../interfaces/FrameFactoryOpts.md) & \{ `pollBit?`: `boolean`; \} |

## Returns

[`Ax25Frame`](../interfaces/Ax25Frame.md)
