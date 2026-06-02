# Strict-vs-pragmatic audit (2026-05-14)

## Why

Packet.NET's philosophy: **spec-compliant by default; pragmatic
accommodations for real-world peers are named opt-in flags in code,
not silent defaults**.

This document inventories every place where the current code accepts
something the strict spec wouldn't require. Each row will be lifted
into a named flag on a `…ParseOptions` record. Defaults stay where
they are today (lenient) — the change is that each accommodation
becomes discoverable, individually toggleable, and documented.

Outcome of this audit feeds the design of `Ax25ParseOptions` /
`AprsParseOptions` and the per-implementation presets (`Strict`,
`Bpq`, `Xrouter`, `Direwolf`, `AprsIs`).

## Packet.Core

Single pragmatic choice. The CRC and the bulk of `Ax25Address` are
spec-pure.

| Location | What we accept | Strict spec says | Driver | Flag name | Default |
|---|---|---|---|---|---|
| `Callsign.cs:30` constructor | `Base.Length` 0–6 | §3.12 "the call sign is made up of upper-case alpha and numeric ASCII characters only" — singular *characters*, plural, implying ≥1. §6.1.1 "operation with destination addresses other than actual amateur call signs is a subject for further study" acknowledges it happens but doesn't bless it. | BPQ's `>IS` ID beacons + PD4R-12 QRV broadcasts (all-space wire slots). PR #85 / 42 frames in a 36 h corpus. | `AllowEmptyCallsignBase` | `true` |

`Callsign.Parse` / `Callsign.TryParse` stay strict by design — they
parse user-typed input where empty is a typo. The flag only affects
the wire-parse path through `Ax25Address.Read`.

## Packet.Ax25

The frame parser is mostly strict. One genuine pragmatic
accommodation, plus a couple of borderline interpretation choices and
some gaps in spec-enforcement that aren't *pragmatic* per se but
should be tracked.

### Pragmatic accommodations

| Location | What we accept | Strict spec says | Driver | Flag name | Default |
|---|---|---|---|---|---|
| `Ax25Frame.cs:344–348` `TryParse` else-branch | Capture trailing bytes as `Info` on **any** non-I/UI frame, including S frames | §3.5 "The Information Field follows the Control Field in I-frames, and the Control or PID field in UI, FRMR, XID and TEST frames." S frames are not in that list — they have no info field. | Defensive: corrupted S frames sometimes carry trailing bytes; FRMR/XID/TEST legitimately have info. Capturing-all sidesteps "which U-frame is allowed an info field" classification. **Note (2026-06-02):** accepting the info at parse no longer means silently processing the frame as valid — `Ax25FrameClassifier` now classifies an info-bearing S frame or no-info U frame (SABM/SABME/DISC/UA/DM) as `InfoNotPermittedInFrame` (DL-ERROR M), so the figc4.x "information not permitted" error-input transition fires at the data-link layer. Strict still rejects the frame outright at decode. | `AllowInfoOnSupervisoryFrames` | `true` |

### Spec interpretations (not pragmatic)

| Location | Choice | Why it's not a flag |
|---|---|---|
| `Ax25FrameClassifier.cs:79` returns `ControlFieldError` for unknown control bytes | The spec mandates a response (FRMR per §4.3.3.9), not a parse rejection. Emitting an event is the correct way to surface this to the session machine, which then sends FRMR. | Spec-compliant. |
| `Ax25Frame.IsUi` / `PollFinal` / `IsCommand` / `IsResponse` mask the right bits per §4.2 / §6.1.2 | Per spec; the masking is the decoding rule, not pragmatism. | — |

### Missing strictness (not pragmatism; under-enforcement)

These aren't pragmatic *choices* — they're checks the parser could be
making but isn't. Worth tracking separately so we don't conflate
"deliberately accept" with "didn't bother to reject".

| Location | What's missing | Spec ref | Severity |
|---|---|---|---|
| `Ax25Frame.TryParse` | No reject of S-frames that carry a PID byte | §3.5 | Medium — misclassified S would be accepted with a phantom PID. (Connected to `AllowInfoOnSupervisoryFrames` above — same code branch.) |
| `Ax25Frame.TryParse` | No length check ≤256 info bytes on I/UI | §3.5 (default paclen 256) | Low — corpus shows all payloads ≤240B; spec allows negotiated paclen >256 via XID anyway. |
| `Ax25Frame.TryParse` | No mod-128 (SABME-initiated session) support | §4.2.2 | N/A — separate work item, not a flag. |
| `Ax25Frame.Factories.cs` | No reject of SABME construction when caller expects mod-8 context | §4.3.3.2 | Low — never observed in BPQ corpus; SABME is the construction-time concern, not parser. |

### XID information-field codec (`XidInfoField` — §4.3.3.7 / Fig 4.5)

The XID parameter-negotiation TLV codec (added with the v2.2 arc V3, part 1). The encoder (`XidInfoField.Encode`) is unconditionally strict — it never emits a malformed information field. The parser's two leniency knobs live on `XidParseOptions` (defaulted off; `XidParseOptions.Lenient` turns both on). These are *new* acceptance, so the default is strict per the CLAUDE.md rule.

| Location | What we accept | Strict spec says | Driver | Flag name | Default |
|---|---|---|---|---|---|
| `XidInfoField.TryParse` GL check | A Group Length that claims more parameter bytes than the buffer holds (clamp to available) | §4.3.3.7 ¶1021 — GL is the exact parameter-field length | Defensive: a peer that mis-sizes GL or appends garbage past the parameter field. No confirmed corpus yet (XID is BPQ-only on our interop matrix); seeded off until interop verifies a real driver. | `AllowGroupLengthOverrun` | `false` |
| `XidInfoField.TryParse` PI/PL check | A trailing PI with no PL octet, or a PL whose PV runs past the parameter field (take what remains) | §4.3.3.7 ¶1023 — the parameter field is an exact run of complete PI/PL/PV triples | As above — tolerate a truncated trailing parameter. Seeded off until interop verifies a real driver. | `AllowTruncatedParameter` | `false` |

Note: unrecognised PIs, `PL=0` (PV-absent ⇒ default), and the ISO-8885-only Tx variants (PI=5/7) are **not** leniency — §4.3.3.7 ¶1024 mandates skipping unknown PIs and treating an absent/zero-length parameter as "use the current/default value". The strict parser does all of this; it's spec-compliant behaviour, not a flag.

### XID Classes-of-Procedures ABM bit (spec worked-example defect)

| Location | Choice | Why it's not a flag |
|---|---|---|
| `ClassesOfProcedures.ToOctets` puts Balanced-ABM at **bit 0** (half-duplex ⇒ `0x21 0x00`) | Figure 4.5's table and §6.3.2 ¶1077 ("Bit 0 is always a 1") put ABM at bit 0. Figure 4.6's worked example instead shows `0x22 0x00` — it placed the always-1 ABM bit at position 1. (Figure 4.6's HDLC-Optional-Functions field, by contrast, *is* table-faithful: ext-addr=bit7, TEST=bit13, 16-bit-FCS=bit15, sync-Tx=bit17 all land exactly per the Fig 4.5 table.) We follow the normative table/prose, so the ABM bit is 0 and half-duplex encodes `0x21`. | Spec-table-vs-figure contradiction, like the APRS `h`-timestamp row — we follow the table. The duplex selection (bit 5) — the only field a peer reads — is identical either way, so there is no interop consequence. **Upstream**: candidate `packethacking/ax25spec` issue (Fig 4.6 ABM byte should be `0x21`, not `0x22`); flagged for Tom, not filed here. |

## Packet.Aprs

Three pragmatic accommodations, several spec-interpretation choices
(not pragmatism), and one decoder that's entirely spec-strict.

### Pragmatic accommodations

| Location | What we accept | Strict spec says | Driver | Flag name | Default |
|---|---|---|---|---|---|
| `AprsStatusDecoder.cs:55` `Encoding.UTF8.GetString` | UTF-8 multi-byte sequences in status text; invalid bytes get replacement char | APRS101 §16 "any printable ASCII characters except `\|` or `~`" — codes 33–126 only | Chinese-station status beacons emit UTF-8 in the APRS-IS firehose | `AllowNonAsciiStatusText` | `true` |
| `AprsTelemetryDecoder.cs:67–73` | Analog values parsed as `double`; accepts `0`, `184`, `3.2`, etc. | APRS101 §13 "five 8-bit unsigned analog data values (expressed as 3-digit decimal numbers in the range 000–255)" | ~30% of corpus telemetry is variable-width int or float (SimplexLogic, RepeaterLogic, BPQ etc.) | `AllowNonIntegerTelemetry` | `true` |
| `AprsMicEDecoder.cs:42` | DTI bytes `0x1C` and `0x1D` in addition to `` ` `` (0x60) and `'` (0x27) | APRS101 §10 lists `` ` `` and `'` (and notes `0x1C` / `0x1D` are "Rev. 0 beta units only") | Early Kenwood firmware still exists on the air | `AllowMicELegacyDtiBytes` | `true` |

### `AprsCallsign` — by-design permissive type

| Location | Behaviour |
|---|---|
| `AprsCallsign.cs` | Accepts 1–9 char bases (vs strict 1–6), letter SSIDs (`-R`, `-D`, `-B`, etc.), lowercase, all-numeric. Used at the **monitor / display** layer for the ~31% of APRS-IS traffic that doesn't fit `Callsign`. Outbound frame construction still uses strict `Callsign`. |

`AprsCallsign` is the existing precedent for the "strict outbound /
permissive inbound" split. It's not a pragmatic *flag* — it's a
parallel type. No retrofit needed; the type itself documents its
intent.

### Spec interpretations (not pragmatic)

| Location | Choice | Why it's not a flag |
|---|---|---|
| `AprsPositionDecoder.TryParseLatitude/Longitude` accept ASCII space in minute/hundredth positions | APRS101 §6 explicitly defines position ambiguity via spaces. Our acceptance is the spec-defined feature. | Spec-blessed. |
| `AprsObjectDecoder` accepts `h` HMS timestamp | APRS101 §11's diagram column header says "Time DHM/HMS"; the prose says "DHM only" but is contradicted by the format diagram. Real-world frames use `h`. | Spec-table says it's legal; prose disagrees. We follow the table. |
| `AprsTelemetryDecoder` optional comma after `MIC` sequence | APRS101 §13 prose: "there may or may not be a comma preceding the first analog data value" | Explicitly spec-blessed both ways. |
| `AprsMicEDecoder` SP+28 / DC+28 dual encodings | APRS101 §10 documents both "old" (non-printable prefix) and "new" (printable prefix) encodings; the §10 worked example shows the unified decode rule. | Spec-blessed both. |

### Decoders that are spec-strict

- `AprsMessageDecoder` — no pragmatic choices found.
- `AprsItemDecoder` — strict spec, name-length window `[3, 9]` per §11.
- `AprsObjectDecoder` apart from the `h` timestamp (which is an
  interpretation, not pragmatism).

## Summary table: flags × presets

The presets we agreed to seed are `Strict`, `Bpq`, `Xrouter`,
`Direwolf`, and `AprsIs`. Mapping the four flags found above:

| Flag | `Strict` | `Bpq` | `Xrouter` | `Direwolf` | `AprsIs` |
|---|:-:|:-:|:-:|:-:|:-:|
| `AllowEmptyCallsignBase` | ✗ | ✓ | ? | ? | ? |
| `AllowInfoOnSupervisoryFrames` | ✗ | ✓ | ? | ? | n/a |
| `AllowNonAsciiStatusText` | ✗ | n/a | n/a | ✓ | ✓ |
| `AllowNonIntegerTelemetry` | ✗ | n/a | n/a | ✓ | ✓ |
| `AllowMicELegacyDtiBytes` | ✗ | n/a | n/a | ✓ | ✓ |

`?` = not yet verified for that implementation (needs a targeted
corpus or a test against the live container before we commit).
`n/a` = flag doesn't apply to that layer (e.g. the BPQ AX.25-layer
preset doesn't care about APRS status text encoding).

`Xrouter` is mostly TBD — we don't have a corpus of frames emitted
by Xrouter yet. Seed it as a copy of `Strict` and let it grow as we
discover specific quirks during interop testing.

## What's not in scope of this audit

- The session machine (`Packet.Ax25.Session`) — strictness questions
  there belong to the dispatcher / SDL transcription work, not to
  parsing. Re-audit once figc4.7 is transcribed.
- The KISS layer (`Packet.Kiss`) — the framing is a transport
  protocol, not AX.25. ACKMODE / multi-drop KISS extensions are
  separately specified (`docs/plan.md` references).
- Outbound frame construction — `Callsign` strict constructor /
  `Ax25Frame.Ui()` etc. are the production path; we want these to
  stay strict so we never produce non-spec frames.

## Next steps

1. **Options shape** (next PR): introduce `Ax25ParseOptions` and
   `AprsParseOptions` records with the flags listed above, plus the
   five preset static factories. Add the types but don't wire them
   in yet so the PR is small and reviewable.
2. **Retrofit `Packet.Core`**: lift `AllowEmptyCallsignBase` into
   `Ax25Address.Read(span, options)` and have the parameterless
   overload use the current lenient default.
3. **Retrofit `Packet.Ax25`**: lift `AllowInfoOnSupervisoryFrames`
   into `Ax25Frame.TryParse(span, options, out frame)`.
4. **Retrofit `Packet.Aprs`**: the three flags above + thread
   options through `TryDecode` overloads on each decoder.
5. **Verify the `?` cells**: once `Xrouter` and `Direwolf` AX.25-layer
   coverage is on, run targeted tests to populate the
   `AllowEmptyCallsignBase` and `AllowInfoOnSupervisoryFrames`
   columns for those presets.
6. **CLAUDE.md update**: codify the rule "when accepting something
   the strict spec doesn't require, surface a named flag — don't
   silently widen the parser".

## Session quirks (SDL-figure deviations — `Ax25SessionQuirks`)

Distinct from the wire-parse pragmatism above: these are *session-layer* deviations from the AX.25 SDL **figures**, used where a figure is a confirmed upstream spec **defect**. The SDL tables (`Packet.Ax25.Sdl`, from `m0lte/ax25sdl`) stay faithful to the published figures — defects and all — so the canonical transcription tracks the in-progress draft; the runtime corrects provable figure errors here, behind named flags. Each flag is named `Ax25Spec<issue>…` after the `packethacking/ax25spec` issue it works around (greppable, removable once the spec is fixed). The default preset is spec-correct (quirks on); `Ax25SessionQuirks.StrictlyFaithful` runs the figures exactly as drawn for conformance testing.

| Flag | What the figure draws | Correct behaviour | Evidence | Default | Remove when |
|---|---|---|---|---|---|
| `Ax25Spec38SrejSelectiveRetransmit` | figc4.5 SREJ-received: generic "Push frame onto queue" + go-back-N "Invoke Retransmission" | single-frame selective retransmit (figc4.4 "Push Old I Frame N(r) on Queue", no Invoke Retransmission). Scope: fires only on the SREJ *response* paths (which carry the push); the SREJ *command* paths carry only Invoke Retransmission, so under the quirk they retransmit nothing — correct, since SREJ is response-only per §4.3.2.4 and no deployed stack sends/acts on a command-form SREJ (packet.net#234). | §4.3.2.4/§6.4.8 + figc4.4 + all four surveyed impls (direwolf/linbpq selective; linux/rax25 none) + direwolf author's "2006 cut-n-paste" erratum; direwolf "Command path has been omitted because SREJ can only be response" (ax25_link.c) + linbpq `if (MSGFLAG & RESP)` (L2Code.c) for the response-only point. Upstream: ax25spec#38; engages-end-to-end + paced-convergence verified in packet.net#233. | `true` | ax25sdl ships a corrected figc4.5 (tracked: packet.net#227) |
