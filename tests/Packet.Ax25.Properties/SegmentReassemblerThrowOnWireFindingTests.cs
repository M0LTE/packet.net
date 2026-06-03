using AwesomeAssertions;
using Packet.Ax25;
using Packet.Ax25.Session;
using Packet.Core;
using Xunit;

namespace Packet.Ax25.Properties;

/// <summary>
/// Pinned regression tests for the SP-004 v2.2-fuzz finding of 2026-06-03:
/// a hostile / malformed segment sequence delivered as PID-0x08 I-frames reaches
/// <see cref="SegmentationLayer.OnDataIndication"/> on the receive path and makes
/// <see cref="Reassembler.Push"/> throw, rather than rejecting the bad segment
/// cleanly. See <c>tools/Packet.Fuzz/FINDINGS.md</c> 2026-06-03 for the full
/// write-up.
/// </summary>
/// <remarks>
/// <para>
/// <b>Status: flagged, not fixed.</b> The throw is the <i>documented and tested</i>
/// contract of <see cref="Reassembler.Push"/> (three asserts in
/// <c>tests/Packet.Ax25.Tests/Session/SegmenterTests.cs</c> pin it). Changing it
/// — whether by making <c>Reassembler.Push</c> swallow protocol violations, or by
/// making <see cref="SegmentationLayer"/> catch and drop / reset / raise a
/// DL-ERROR — is a <b>behavioural protocol decision</b>, not an obvious bounds-check
/// fix, so per the workstream rules it is reported rather than guessed at.
/// </para>
/// <para>
/// <b>Why it isn't a live crash today:</b> the one production caller,
/// <see cref="Ax25Listener"/>, wraps its inbound dispatch in a catch-all
/// (<c>Ax25Listener.cs</c> ≈ line 350, "swallowed: see Note on event-handler
/// exceptions"), so the throw is swallowed and the read loop survives. The cost
/// is silent: the malformed segment's whole DL-DATA indication is dropped, and a
/// reassembler left mid-series can mis-react to a subsequent valid continuation.
/// </para>
/// <para>
/// These tests pin the <b>current</b> throwing behaviour at the wire seam. If the
/// behaviour is deliberately changed, they will fail and force whoever changes it
/// to update this file and the finding together — the point of a pinned
/// regression.
/// </para>
/// </remarks>
public class SegmentReassemblerThrowOnWireFindingTests
{
    private static SegmentationLayer NewWireLayer(Ax25SessionQuirks? quirks = null)
    {
        var ctx = new Ax25SessionContext
        {
            Local = new Callsign("M0LTE", 0),
            Remote = new Callsign("G7XYZ", 7),
            Quirks = quirks ?? Ax25SessionQuirks.Default,
        };
        return new SegmentationLayer(ctx);
    }

    private static DataLinkDataIndication Seg(params byte[] infoField)
        => new(infoField, Ax25Frame.PidSegmented);

    /// <summary>
    /// Case 1 — a non-First segment with no prior First reaches the wire seam and
    /// throws <see cref="System.InvalidOperationException"/>.
    /// Hex input (segment info field): <c>05 AA BB</c> (First bit clear,
    /// remaining-count = 5).
    /// </summary>
    [Fact]
    public void Wire_NonFirst_Without_Prior_First_Throws_InvalidOperation()
    {
        var layer = NewWireLayer();
        var act = () => layer.OnDataIndication(Seg(0x05, 0xAA, 0xBB));

        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*non-First*",
                "FINDING 2026-06-03: a missing-first segment off the wire throws rather than being dropped");
    }

    /// <summary>
    /// Case 2 — an empty info field on a PID-0x08 indication reaches the wire seam
    /// and throws <see cref="System.ArgumentException"/>.
    /// Hex input (segment info field): <c>(empty)</c>.
    /// </summary>
    [Fact]
    public void Wire_Empty_Segment_Info_Field_Throws_Argument()
    {
        var layer = NewWireLayer();
        var act = () => layer.OnDataIndication(Seg());

        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*at least 1 byte*",
                "FINDING 2026-06-03: an empty 0x08-PID info field off the wire throws rather than being rejected");
    }

    /// <summary>
    /// Case 3 — under the default (inner-PID) quirk, a First segment carrying only
    /// the F/X octet (no inner-PID octet) reaches the wire seam and throws
    /// <see cref="System.ArgumentException"/>.
    /// Hex input (segment info field): <c>80</c> (First bit set, remaining-count = 0,
    /// no inner-PID octet).
    /// </summary>
    [Fact]
    public void Wire_InnerPid_First_Missing_Pid_Octet_Throws_Argument()
    {
        var layer = NewWireLayer(Ax25SessionQuirks.Default);   // SegmentFirstCarriesL3Pid = true
        var act = () => layer.OnDataIndication(Seg(0x80));

        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*inner-PID*",
                "FINDING 2026-06-03: an inner-PID First lacking its PID octet, off the wire, throws");
    }

    /// <summary>
    /// Case 4 — an out-of-sequence continuation (a valid First, then a segment
    /// whose remaining-count skips a value) throws
    /// <see cref="System.InvalidOperationException"/>.
    /// Hex inputs: First <c>85 CC</c> (StrictlyFaithful so no inner PID is read;
    /// First bit + remaining = 5), then <c>03 DD</c> (remaining = 3 where 4 was
    /// expected).
    /// </summary>
    [Fact]
    public void Wire_Out_Of_Sequence_Continuation_Throws_InvalidOperation()
    {
        // Use StrictlyFaithful so the first segment isn't consumed as inner-PID.
        var layer = NewWireLayer(Ax25SessionQuirks.StrictlyFaithful);

        // First segment: First bit set, remaining 5 — accepted, starts the series.
        layer.OnDataIndication(Seg(Segmenter.FirstBit | 5, 0xCC)).Should().BeNull("mid-series, nothing delivered yet");

        // Continuation with remaining 3 (expected 4) — out of sequence.
        var act = () => layer.OnDataIndication(Seg(3, 0xDD));
        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*out of sequence*",
                "FINDING 2026-06-03: an out-of-sequence continuation off the wire throws");
    }

    /// <summary>
    /// Sanity counterpart: a well-formed single-segment series at the wire seam
    /// does <b>not</b> throw and delivers the reassembled payload. Confirms the
    /// finding is specifically about <i>malformed</i> input, not the happy path.
    /// </summary>
    [Fact]
    public void Wire_WellFormed_Single_Segment_Does_Not_Throw_And_Delivers()
    {
        var layer = NewWireLayer(Ax25SessionQuirks.StrictlyFaithful);   // figure-literal: no inner PID

        // First + last in one: First bit set, remaining 0, one data byte.
        var delivered = layer.OnDataIndication(Seg(Segmenter.FirstBit | 0, 0x42));

        delivered.Should().NotBeNull();
        delivered!.Info.ToArray().Should().Equal(0x42);
        delivered.Pid.Should().Be(SegmentationLayer.FigureLiteralReassembledPid);
    }
}
