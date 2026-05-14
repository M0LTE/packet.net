using AwesomeAssertions;
using Packet.Ax25;
using Packet.Ax25.Session;
using Packet.Core;

namespace Packet.Ax25.Tests.Session;

public class Ax25FrameClassifierTests
{
    private static readonly Callsign Local  = new("M0LTE", 0);
    private static readonly Callsign Remote = new("G7XYZ", 7);

    // ─── U-frame classification (mirrors the §4.3.3 control bytes) ─────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sabm_Classifies_To_SabmReceived(bool pollBit)
    {
        var frame = Ax25Frame.Sabm(Local, Remote, pollBit);
        Ax25FrameClassifier.Classify(frame).Should().BeOfType<SabmReceived>()
            .Which.Frame.Should().Be(frame);
    }

    [Fact]
    public void Sabme_Classifies_To_SabmeReceived()
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Sabme(Local, Remote))
            .Should().BeOfType<SabmeReceived>();
    }

    [Fact]
    public void Disc_Classifies_To_DiscReceived()
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Disc(Local, Remote))
            .Should().BeOfType<DiscReceived>();
    }

    [Fact]
    public void Ua_Classifies_To_UaReceived()
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Ua(Local, Remote))
            .Should().BeOfType<UaReceived>();
    }

    [Fact]
    public void Dm_Classifies_To_DmReceived()
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Dm(Local, Remote))
            .Should().BeOfType<DmReceived>();
    }

    [Fact]
    public void Frmr_Classifies_To_FrmrReceived()
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Frmr(Local, Remote, info: stackalloc byte[] { 0, 0, 0 }))
            .Should().BeOfType<FrmrReceived>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Xid_Classifies_To_XidReceived(bool isCommand)
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Xid(Local, Remote, info: ReadOnlySpan<byte>.Empty, isCommand))
            .Should().BeOfType<XidReceived>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Test_Classifies_To_TestReceived(bool isCommand)
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Test(Local, Remote, info: ReadOnlySpan<byte>.Empty, isCommand))
            .Should().BeOfType<TestReceived>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ui_Classifies_To_UiReceived(bool pollFinal)
    {
        var frame = Ax25Frame.Ui(Local, Remote, info: "hi"u8, pollFinal: pollFinal);
        Ax25FrameClassifier.Classify(frame).Should().BeOfType<UiReceived>();
    }

    // ─── S-frame classification (§4.3.2) ───────────────────────────────

    [Theory]
    [InlineData(0, false)]
    [InlineData(5, true)]
    [InlineData(7, false)]
    public void Rr_Classifies_To_RrReceived_Regardless_Of_Nr_And_Pf(byte nr, bool pollFinal)
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Rr(Local, Remote, nr, isCommand: false, pollFinal))
            .Should().BeOfType<RrReceived>();
    }

    [Fact]
    public void Rnr_Classifies_To_RnrReceived()
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Rnr(Local, Remote, nr: 3, isCommand: false))
            .Should().BeOfType<RnrReceived>();
    }

    [Fact]
    public void Rej_Classifies_To_RejReceived()
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Rej(Local, Remote, nr: 2, isCommand: false))
            .Should().BeOfType<RejReceived>();
    }

    [Fact]
    public void Srej_Classifies_To_SrejReceived()
    {
        Ax25FrameClassifier.Classify(Ax25Frame.Srej(Local, Remote, nr: 1, isCommand: false))
            .Should().BeOfType<SrejReceived>();
    }

    // ─── I-frame classification (§4.3.1) ───────────────────────────────

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(5, 3, true)]
    [InlineData(7, 7, false)]
    public void I_Frame_Classifies_To_IFrameReceived_Regardless_Of_Seq_Vars(byte nr, byte ns, bool pollBit)
    {
        var frame = Ax25Frame.I(Local, Remote, nr, ns, info: "x"u8, pollBit: pollBit);
        Ax25FrameClassifier.Classify(frame).Should().BeOfType<IFrameReceived>()
            .Which.Frame.Should().Be(frame);
    }

    // ─── Malformed / unrecognised ──────────────────────────────────────

    [Theory]
    [InlineData(0x17)]  // U-frame shape (bits 1-0 = 11) but bits 7-5/3-2 don't match any known type
    [InlineData(0xC3)]
    [InlineData(0xFB)]
    public void Unknown_U_Frame_Control_Byte_Maps_To_ControlFieldError(byte unknownControl)
    {
        // Build a frame with a custom control byte by parsing hand-crafted bytes.
        var bytes = new byte[15];
        new Ax25Address(Local,  CrhBit: true,  ExtensionBit: false).Write(bytes.AsSpan(0, 7));
        new Ax25Address(Remote, CrhBit: false, ExtensionBit: true ).Write(bytes.AsSpan(7, 7));
        bytes[14] = unknownControl;
        Ax25Frame.TryParse(bytes, out var frame).Should().BeTrue();

        Ax25FrameClassifier.Classify(frame!).Should().BeOfType<ControlFieldError>();
    }

    // ─── End-to-end ────────────────────────────────────────────────────

    [Fact]
    public void Outbound_Spec_Serialised_And_Reclassified_Round_Trip_Preserves_Frame_Type()
    {
        // Symmetry check: emit → bytes → parse → classify should recover
        // the same frame type. Doesn't test session semantics, just that
        // the wire codec + classifier are inverses on the bit level.
        var ctx = new Ax25SessionContext
        {
            Local  = Local,
            Remote = Remote,
        };
        var spec = new UFrameSpec(UFrameType.Sabm, IsCommand: true, PfBit: true);
        var bytes = spec.ToAx25Frame(ctx).ToBytes();

        Ax25Frame.TryParse(bytes, out var roundTripped).Should().BeTrue();
        Ax25FrameClassifier.Classify(roundTripped!).Should().BeOfType<SabmReceived>();
    }
}
