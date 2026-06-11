using Packet.Node.Core.Api;
using Packet.Node.Core.Traffic;

namespace Packet.Node.Tests.Traffic;

/// <summary>
/// The <see cref="MonitorEvent"/> → <see cref="TrafficRecord"/> projection: the
/// monitor's "in"/"out" becomes the log's "rx"/"tx", the "0xCF"-style PID string
/// becomes the numeric byte, the timestamp is UTC-normalised, and the raw dump is
/// capped at <see cref="SqliteTrafficStore.RawCapBytes"/> while the decoded
/// columns (info length included) stay full-fidelity.
/// </summary>
[Trait("Category", "Node")]
public sealed class TrafficRecordTests
{
    private static MonitorEvent Evt(
        string direction = "in", string type = "I", string? pid = "0xCF",
        int? ns = 3, int? nr = 5, int rawLength = 20)
        => new(
            Seq: 1,
            Timestamp: new DateTimeOffset(2026, 6, 11, 13, 0, 0, TimeSpan.FromHours(2)),
            PortId: "vhf",
            Direction: direction,
            Source: "M0LTE-1",
            Dest: "G7XYZ-2",
            Type: type,
            ClassKind: "I",
            Pid: pid,
            PidName: null,
            Ns: ns,
            Nr: nr,
            Pf: 1,
            Command: true,
            Length: rawLength,
            Summary: "I N(S)=3 N(R)=5",
            Raw: Enumerable.Range(0, rawLength).Select(i => i & 0xFF).ToArray(),
            Path: [])
        {
            Control = 0x76,
            InfoLength = rawLength - 17,
        };

    [Fact]
    public void Maps_the_monitor_event_onto_the_row_shape()
    {
        var record = TrafficRecord.From(Evt());

        record.PortId.Should().Be("vhf");
        record.Direction.Should().Be("rx", "the monitor's \"in\" is the log's \"rx\"");
        record.Source.Should().Be("M0LTE-1");
        record.Dest.Should().Be("G7XYZ-2");
        record.Kind.Should().Be("I");
        record.Ns.Should().Be(3);
        record.Nr.Should().Be(5);
        record.Pf.Should().Be(1);
        record.Control.Should().Be(0x76);
        record.Pid.Should().Be(0xCF, "the \"0xCF\" string becomes the numeric byte");
        record.InfoLength.Should().Be(3);
        record.Raw.Should().Equal(Enumerable.Range(0, 20).Select(i => (byte)i));
    }

    [Fact]
    public void Out_becomes_tx_and_a_missing_pid_stays_null()
    {
        var record = TrafficRecord.From(Evt(direction: "out", type: "SABM", pid: null, ns: null, nr: null));

        record.Direction.Should().Be("tx");
        record.Kind.Should().Be("SABM");
        record.Pid.Should().BeNull();
        record.Ns.Should().BeNull();
        record.Nr.Should().BeNull();
    }

    [Fact]
    public void Timestamp_is_normalised_to_utc()
        => TrafficRecord.From(Evt()).TimestampUtc.Should()
            .Be(new DateTimeOffset(2026, 6, 11, 11, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Raw_is_capped_but_info_length_stays_full()
    {
        // A frame bigger than the cap (e.g. a large negotiated N1) stores a
        // truncated dump but keeps the true decoded info length.
        var record = TrafficRecord.From(Evt(rawLength: SqliteTrafficStore.RawCapBytes + 500));

        record.Raw.Should().HaveCount(SqliteTrafficStore.RawCapBytes);
        record.InfoLength.Should().Be(SqliteTrafficStore.RawCapBytes + 500 - 17);
    }
}
