[**@packet-net/ax25**](../../README.md)

***

[@packet-net/ax25](../../README.md) / [index](../README.md) / Ax25ListenerOptions

# Interface: Ax25ListenerOptions

Defined in: listener.ts:22

Options for [Ax25Listener](../classes/Ax25Listener.md). `myCall` is required; everything else
has a sensible default that matches the AX.25 v2.2 spec.

## Properties

| Property | Type | Description | Defined in |
| ------ | ------ | ------ | ------ |
| <a id="configuresession"></a> `configureSession?` | (`session`) => `void` | Optional hook called once per newly-built session, before any events flow into it. Use to attach onData / onDisconnect handlers on the session's signal stream before the SDL processes the inbound SABM that triggered session creation. | listener.ts:53 |
| <a id="k"></a> `k?` | `number` | Override the spec-default k (send-window size; default 4 for mod-8). | listener.ts:40 |
| <a id="maxcachedpeers"></a> `maxCachedPeers?` | `number` | LRU cap on cached per-peer sessions. Default 64 — most node deployments sit well within that; the cap is a memory safety belt to keep a misbehaving / spam-SABM peer from creating unbounded sessions. | listener.ts:46 |
| <a id="mycall"></a> `myCall` | `string` \| [`Callsign`](../classes/Callsign.md) | Local callsign. Inbound frames not addressed here are ignored at the session layer. | listener.ts:24 |
| <a id="n2"></a> `n2?` | `number` | Override the spec-default N2 (max retries; default 10). | listener.ts:38 |
| <a id="onhandlererror"></a> `onHandlerError?` | (`err`) => `void` | Optional sink for event-handler exceptions. The listener wraps every `sessionAccepted` / `frameTraced` dispatch in try/catch so a buggy subscriber can't DoS the inbound pump; exceptions go here. Defaults to `console.error`. | listener.ts:60 |
| <a id="t1ms"></a> `t1Ms?` | `number` | Override the session-context default T1V (acknowledgement timer). If omitted, sessions use the spec default (6 s = 2 × initial SRT); figc4.7's `Select_T1_Value` would recompute the running value dynamically — the TS dispatcher stubs that subroutine so the static value sticks. | listener.ts:32 |
| <a id="t2ms"></a> `t2Ms?` | `number` | Override the session-context default T2 (response-delay timer). Default 1500 ms. | listener.ts:34 |
| <a id="t3ms"></a> `t3Ms?` | `number` | Override the dispatcher's T3 (inactive-link) timer duration. Default 30 000 ms. | listener.ts:36 |
