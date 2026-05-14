# Packet.Kiss.NinoTnc

NinoTNC (N9600A) driver for Packet.NET. Speaks KISS over USB-CDC serial,
with first-class support for:

- The NinoTNC `SETHW` mode-selection extension (mode 0–14 + `+16`
  non-persist offset).
- The G8BPQ `ACKMODE` KISS extension (KISS command `0x0C`) with
  per-tag TX-completion correlation.
- The on-demand "TX-Test" diagnostic frame the modem emits when its
  front-panel button is pressed (firmware version, serial number,
  uptime, packet counters).
- Per-peer adaptive KISS parameters via `Packet.Kiss.Adaptive`
  (currently: `TxDelayHillClimbEstimator`).

This is part of [Packet.NET](https://github.com/M0LTE/packet.net); see
the parent project's [`docs/plan.md`](../../docs/plan.md) for the
big picture.

## Quick start

```csharp
using Packet.Kiss;
using Packet.Kiss.NinoTnc;
using Packet.Ax25;
using Packet.Core;

await using var tnc = NinoTncSerialPort.Open("COM6"); // or "/dev/ttyACM0"
await tnc.SetModeAsync(mode: 6);                       // 1200 AFSK AX.25
                                                       // (non-persist by default)

// Inbound — IAsyncEnumerable
_ = Task.Run(async () =>
{
    await foreach (var frame in tnc.ReadFramesAsync())
    {
        if (frame.Command == KissCommand.Data &&
            Ax25Frame.TryParse(frame.Payload, out var ax25))
        {
            Console.WriteLine($"{ax25.Source.Callsign} → {ax25.Destination.Callsign}");
        }
    }
});

// Outbound
var ui = Ax25Frame.Ui(
    destination: new Callsign("CQ"),
    source: new Callsign("M0LTE", 1),
    info: "hello"u8);
await tnc.SendFrameAsync(ui.ToBytes());
```

## `SetModeAsync` and flash wear

`SetModeAsync(mode)` defaults to **non-persistent**: the TNC's flash is
not touched, and the configured mode reverts on reboot. Pass
`persistToFlash: true` only when the operator wants the choice to
survive a power cycle. Flash has a finite write-cycle budget; tooling
should not burn it on every dev iteration.

## ACKMODE with TX-completion correlation

```csharp
var receipt = await tnc.SendFrameWithAckAsync(
    ui.ToBytes(),
    timeout: TimeSpan.FromSeconds(30));
Console.WriteLine($"tx-complete after {receipt.Elapsed.TotalMilliseconds:F0} ms");
```

The TNC echoes the 2-byte sequence tag back when the frame has been
transmitted (per the multi-drop KISS extension). The driver auto-
assigns tags and correlates the echoes; concurrent calls each get
their own receipt.

## Adaptive transport — per-peer TXDELAY learning

`AdaptiveNinoTncTransport` is the layer that calls into an
`IAdaptiveParameterEstimator` to learn per-peer KISS parameters from
observed outcomes:

```csharp
var estimator = new TxDelayHillClimbEstimator(initialTxDelay: 50);
await using var transport = new AdaptiveNinoTncTransport(tnc, estimator);

// Before each TX the transport asks the estimator for that peer's
// recommended parameters, applies any deltas, sends in ACKMODE, and
// feeds the outcome back.
await transport.SendAsync("M0LTE-9", ui.ToBytes());

// AX.25-layer signals (when the session machine knows a frame was
// retransmitted / lost):
transport.RecordRetransmittedAck("M0LTE-9", payloadBytes: 100);
transport.RecordLoss("M0LTE-9", payloadBytes: 100);
```

The default `TxDelayHillClimbEstimator` walks per-peer TXDELAY down on
consecutive first-try ACKs, ratchets up on loss / ACK timeout, clamps
to a min/max. The other KISS parameters (PERSIST, SLOTTIME, TXTAIL)
pass through unchanged; the estimator interface supports them but
the concrete first-pass implementation does not. Plug in your own
`IAdaptiveParameterEstimator` to do more.

For unit-testing the transport layer without a real TNC, depend on
`INinoTncModem` instead of `NinoTncSerialPort` directly.

## Port discovery

```csharp
foreach (var candidate in NinoTncPortDiscovery.EnumerateCandidates())
{
    Console.WriteLine($"{candidate.PortName}  ({candidate.ResolvedDevicePath})");
}
```

- **Linux**: prefers `/dev/serial/by-id/...` symlinks; falls back to
  `/dev/ttyACM*`.
- **Windows / macOS**: `SerialPort.GetPortNames()`.

USB VID/PID-based filtering is not yet implemented; on hosts where
unrelated USB-CDC devices share VID/PID space with the NinoTNC, set
the `PACKETNET_NINOTNC_PORTS` environment variable to a comma-
separated list of port names to be explicit:

```sh
# Linux
PACKETNET_NINOTNC_PORTS=/dev/ttyACM0,/dev/ttyACM1 ./packetnet ...

# Windows PowerShell
$env:PACKETNET_NINOTNC_PORTS = "COM6,COM8"
```

## TX-Test diagnostic frame

When the operator presses the modem's front-panel TX-Test button, the
modem transmits a test signal over the air *and* sends a synthetic
KISS Data frame to the USB host containing firmware-version + uptime
+ packet counters. Decode it with:

```csharp
tnc.FrameReceived += (_, frame) =>
{
    if (NinoTncTxTestFrame.TryParse(frame, out var diag))
    {
        Console.WriteLine($"firmware {diag.FirmwareVersion}, " +
                          $"running mode {diag.RunningMode?.Name}, " +
                          $"uptime {diag.Uptime}");
    }
};
```

## Operating-mode catalog

`NinoTncCatalog.ByMode` is the DIP-switch-position → mode table for
firmware v3.44; `NinoTncCatalog.FirmwareByteToMode` is the reverse
lookup keyed on the firmware byte the TNC reports in its
`BrdSwchMod` diagnostic field. The catalog is firmware-version-
specific; bump when needed.

## See also

- [`docs/nino-tnc-characterisation.md`](../../docs/nino-tnc-characterisation.md)
  — empirical measurements from the back-to-back NinoTNC pair.
- [`tools/Packet.NinoTnc.Spike`](../../tools/Packet.NinoTnc.Spike/) — the
  spike-and-soak tool that produced those numbers.
- [Multi-drop KISS spec](https://github.com/packethacking/ax25spec/blob/main/doc/multi-drop-kiss-operation.md)
  — authoritative reference for ACKMODE, POLL, the port nibble.
- [NinoTNC wiki](https://wiki.oarc.uk/packet:ninotnc) — operator-facing
  hardware documentation.
