# Packet.Fuzz — findings

SP-004 fuzzer findings log. One entry per smoke / AFL run that surfaced
something. Each entry should include the input bytes (hex) and a stack-trace
summary.

See `Program.cs` for the harness and `corpus/` for the seed inputs.

## 2026-05-15 — initial smoke run

Default smoke configuration, N=1000 per parser, plus the 6 seed-corpus
files and 32 single-bit / single-byte mutations of each seed
(=198 fixed inputs + 1000 generated inputs per parser).

Targets:

- `Ax25Frame.TryParse(ReadOnlySpan<byte>, out _)` — direct AX.25 wire-format
  parser, KISS-form bytes (no flags, no FCS).
- `KissDecoder.Push(ReadOnlySpan<byte>)` — KISS framing decoder. The task
  brief asked for `KissFrame.TryParse`, but `KissFrame` is a plain record
  struct (port + command + payload) with no static parser — KISS is a
  stateful SLIP-style framer, not a one-shot parser, so the equivalent
  harness drives arbitrary bytes through a fresh `KissDecoder` for each
  input.

Result:

```
Packet.Fuzz smoke run: 1000 iterations per parser, seed=0xC0DEFEED

── Ax25Frame.TryParse ──
  1198 inputs (6 seed + 192 seed-mutations + 1000 generated) / 8 ms / 0 unhandled exceptions

── KissDecoder.Push ──
  1198 inputs (6 seed + 192 seed-mutations + 1000 generated) / 8 ms / 0 unhandled exceptions

════════ Summary ════════
  Ax25Frame.TryParse: clean — 1000 inputs, no throws.
  KissDecoder.Push: clean — 1000 inputs, no throws.
```

Extended runs (not part of the default smoke) — same harness with `N=100000`
and across five different RNG seeds (`0xC0DEFEED`, `0x00000001`, `0x0000002A`,
`0x0000270F`, `0x0012D687`, `0xFFFFFFFF`) — were also clean. ~600k total
fuzz inputs hit `Ax25Frame.TryParse`, and the same for `KissDecoder.Push`,
with zero unhandled exceptions in either.

### Interpretation

Both parsers handle malformed input by returning `false` / dropping bytes,
never by throwing — which is the documented contract:

- `Ax25Frame.TryParse` is already defended at its single throw-prone site
  (`Ax25Address.Read` can throw `ArgumentException` on bad address bytes);
  the catch-and-return-false path was added deliberately. There is an
  existing FsCheck property `TryParse_Never_Throws` in
  `tests/Packet.Ax25.Properties/Ax25ParserFuzzProperties.cs` that asserts
  the same thing across 2000 random inputs (amendment log
  2026-05-14 — "ax25: fuzz / property tests for the frame parser"). The
  SharpFuzz harness extends that property to a larger, more structured
  input distribution but reaches the same conclusion.
- `KissDecoder.Push` is by spec lenient (KissDecoder.cs:38–41 — "receivers
  should be lenient with malformed escape sequences. Drop the byte and
  continue."). There is no input that can drive it to throw at the framing
  layer.

So: nothing to fix from this run. Good baseline for future regression
detection — re-running the smoke after any parser change is cheap (<1 sec
for `N=10000`).

### Mutation strategies in the smoke run

The smoke generator mixes seven distributions:

| Strategy | Notes |
|----------|-------|
| Truncated buffer (0..16 bytes) | Probes the minimum-length guard |
| Around-min buffer (14..32 bytes) | Probes address-chain boundary conditions |
| Typical KISS payload (15..350 bytes) | Most-likely-real shape |
| Oversized buffer (350..4096 bytes) | Beyond AX.25 paclen |
| All-same-byte | FEND-only, 0xFF-only, 0x00-only edge cases |
| SLIP-pathological | Heavy bias to FEND / FESC / Tfend / Tfesc to stress the KISS escape state machine |
| Structured-AX25-like | 14-byte address pair + 0..9 digipeaters + ctrl + pid + info |

Plus replay + single-byte / single-bit mutation of the on-disk corpus.

## How to re-run

```sh
# Default smoke (1000 inputs per parser).
dotnet run --project tools/Packet.Fuzz -- --smoke

# Larger sweep with custom seed.
dotnet run --project tools/Packet.Fuzz -- --smoke 100000 1234

# Regenerate the seed corpus from current Ax25Frame factories.
dotnet run --project tools/Packet.Fuzz -- --seed-corpus tools/Packet.Fuzz/corpus

# AFL/libfuzzer harness (requires afl-fuzz + libfuzzer-dotnet on PATH).
afl-fuzz -i tools/Packet.Fuzz/corpus/ax25 -o /tmp/ax25-out -- \
    dotnet tools/Packet.Fuzz/bin/Debug/net10.0/Packet.Fuzz.dll ax25 @@
```

## Format for future entries

```
## YYYY-MM-DD — short title

Run config (N, seed, parser target).

Findings:

- input hex (truncated to 64B if huge): `D408D08C8E8094E68C8C…`
- exception type + first stack frame: `IndexOutOfRangeException at Ax25Address.Read`
- short prose explaining the root cause, if known
```
