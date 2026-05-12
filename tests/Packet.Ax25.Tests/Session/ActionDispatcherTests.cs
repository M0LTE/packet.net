using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Packet.Ax25.Session;
using Packet.Core;

namespace Packet.Ax25.Tests.Session;

public class ActionDispatcherTests
{
    private static (ActionDispatcher dispatcher,
                    Ax25SessionContext ctx,
                    SystemTimerScheduler scheduler,
                    FakeTimeProvider time,
                    List<string> timerExpiries,
                    List<SupervisoryFrameSpec> sFrames) NewRig()
    {
        var timerExpiries = new List<string>();
        var sFrames = new List<SupervisoryFrameSpec>();
        var time = new FakeTimeProvider();
        var scheduler = new SystemTimerScheduler(time);
        var dispatcher = new ActionDispatcher(
            onTimerExpiry: timerExpiries.Add,
            sendSFrame: sFrames.Add);
        var ctx = new Ax25SessionContext
        {
            Local  = new Callsign("M0LTE", 0),
            Remote = new Callsign("G7XYZ", 7),
        };
        return (dispatcher, ctx, scheduler, time, timerExpiries, sFrames);
    }

    // ─── Flag mutations ────────────────────────────────────────────────

    [Fact]
    public void Set_Own_Receiver_Busy_Sets_The_Flag()
    {
        var (d, ctx, s, _, _, _) = NewRig();
        ctx.OwnReceiverBusy.Should().BeFalse();
        d.Execute("set_own_receiver_busy", ctx, s);
        ctx.OwnReceiverBusy.Should().BeTrue();
    }

    [Fact]
    public void Clear_Own_Receiver_Busy_Clears_The_Flag()
    {
        var (d, ctx, s, _, _, _) = NewRig();
        ctx.OwnReceiverBusy = true;
        d.Execute("clear_own_receiver_busy", ctx, s);
        ctx.OwnReceiverBusy.Should().BeFalse();
    }

    [Theory]
    [InlineData("set_acknowledge_pending",   nameof(Ax25SessionContext.AcknowledgePending), true)]
    [InlineData("clear_acknowledge_pending", nameof(Ax25SessionContext.AcknowledgePending), false)]
    [InlineData("set_layer_3_initiated",     nameof(Ax25SessionContext.Layer3Initiated),    true)]
    [InlineData("clear_layer_3_initiated",   nameof(Ax25SessionContext.Layer3Initiated),    false)]
    [InlineData("set_peer_receiver_busy",    nameof(Ax25SessionContext.PeerReceiverBusy),   true)]
    [InlineData("clear_peer_receiver_busy",  nameof(Ax25SessionContext.PeerReceiverBusy),   false)]
    public void Flag_Verbs_Mutate_The_Right_Field(string action, string fieldName, bool expectedValue)
    {
        var (d, ctx, s, _, _, _) = NewRig();
        // Set the opposite to make the change observable
        typeof(Ax25SessionContext).GetProperty(fieldName)!.SetValue(ctx, !expectedValue);

        d.Execute(action, ctx, s);

        typeof(Ax25SessionContext).GetProperty(fieldName)!.GetValue(ctx).Should().Be(expectedValue);
    }

    // ─── Timer operations ──────────────────────────────────────────────

    [Theory]
    [InlineData("start_T1", "T1")]
    [InlineData("start_T2", "T2")]
    [InlineData("start_T3", "T3")]
    public void Start_Timer_Arms_The_Named_Timer(string action, string timerName)
    {
        var (d, ctx, s, _, _, _) = NewRig();
        d.Execute(action, ctx, s);
        s.IsRunning(timerName).Should().BeTrue();
    }

    [Theory]
    [InlineData("stop_T1", "T1")]
    [InlineData("stop_T2", "T2")]
    [InlineData("stop_T3", "T3")]
    public void Stop_Timer_Cancels_The_Named_Timer(string action, string timerName)
    {
        var (d, ctx, s, _, _, _) = NewRig();
        // Arm first so cancel has something to clear
        d.Execute("start_" + timerName, ctx, s);
        s.IsRunning(timerName).Should().BeTrue();

        d.Execute(action, ctx, s);
        s.IsRunning(timerName).Should().BeFalse();
    }

    [Fact]
    public void Timer_Expiry_Calls_The_Configured_Callback_With_The_Timer_Name()
    {
        var (d, ctx, s, time, expiries, _) = NewRig();
        d.Execute("start_T1", ctx, s);

        time.Advance(d.T1Duration);

        expiries.Should().ContainSingle().Which.Should().Be("T1");
    }

    [Fact]
    public void Default_Timer_Durations_Match_The_Spec_Defaults()
    {
        var (d, _, _, _, _, _) = NewRig();
        // T1 default 3000 ms (XID PI=9 default), T3 default chosen per §6.7.1.3.
        d.T1Duration.Should().Be(TimeSpan.FromMilliseconds(3000));
        d.T2Duration.Should().Be(TimeSpan.FromMilliseconds(1500));
        d.T3Duration.Should().Be(TimeSpan.FromMilliseconds(30000));
    }

    // ─── Queue operations ──────────────────────────────────────────────

    [Fact]
    public void Discard_I_Frame_Queue_Empties_The_Queue()
    {
        var (d, ctx, s, _, _, _) = NewRig();
        ctx.IFrameQueue.Enqueue(new byte[] { 1 });
        ctx.IFrameQueue.Enqueue(new byte[] { 2 });
        ctx.IFrameQueue.Should().HaveCount(2);

        d.Execute("discard_i_frame_queue", ctx, s);

        ctx.IFrameQueue.Should().BeEmpty();
    }

    // ─── Supervisory-frame transmissions ───────────────────────────────

    [Theory]
    [InlineData("RR command",   SupervisoryFrameType.Rr,   true)]
    [InlineData("RR response",  SupervisoryFrameType.Rr,   false)]
    [InlineData("RNR command",  SupervisoryFrameType.Rnr,  true)]
    [InlineData("RNR response", SupervisoryFrameType.Rnr,  false)]
    [InlineData("REJ command",  SupervisoryFrameType.Rej,  true)]
    [InlineData("REJ response", SupervisoryFrameType.Rej,  false)]
    [InlineData("SREJ command", SupervisoryFrameType.Srej, true)]
    [InlineData("SREJ response", SupervisoryFrameType.Srej, false)]
    public void Supervisory_Verbs_Signal_Outgoing_Frame_With_Right_Type_And_Role(
        string action, SupervisoryFrameType expectedType, bool expectedIsCommand)
    {
        var (d, ctx, s, _, _, sFrames) = NewRig();
        d.Execute(action, ctx, s);

        sFrames.Should().ContainSingle();
        sFrames[0].Type.Should().Be(expectedType);
        sFrames[0].IsCommand.Should().Be(expectedIsCommand);
    }

    // ─── Sequence-variable assignments ─────────────────────────────────

    [Fact]
    public void RC_Assignment_Resets_To_Zero()
    {
        var (d, ctx, s, _, _, _) = NewRig();
        ctx.RC = 7;
        d.Execute("RC := 0", ctx, s);
        ctx.RC.Should().Be(0);
    }

    [Fact]
    public void VS_Increment_Wraps_At_Modulus()
    {
        var (d, ctx, s, _, _, _) = NewRig();
        ctx.VS = 7;
        d.Execute("V(S) := V(S) + 1", ctx, s);
        ctx.VS.Should().Be((byte)0, "mod-8 by default; 7 + 1 wraps to 0");
    }

    [Fact]
    public void VR_Increment_Wraps_At_Modulus()
    {
        var (d, ctx, s, _, _, _) = NewRig();
        ctx.VR = 7;
        d.Execute("V(R) := V(R) + 1", ctx, s);
        ctx.VR.Should().Be((byte)0);
    }

    // ─── Bulk execute + error path ─────────────────────────────────────

    [Fact]
    public void Bulk_Execute_Runs_The_Whole_Action_Chain_In_Order()
    {
        // The actual t01_dl_flow_off_when_own_receiver_busy chain from
        // figc4.4a col 5 (Yes branch).
        var (d, ctx, s, _, _, sFrames) = NewRig();
        d.Execute(
            new[] { "set_own_receiver_busy", "RNR response", "clear_acknowledge_pending" },
            ctx, s);

        ctx.OwnReceiverBusy.Should().BeTrue();
        ctx.AcknowledgePending.Should().BeFalse();
        sFrames.Should().ContainSingle();
        sFrames[0].Type.Should().Be(SupervisoryFrameType.Rnr);
        sFrames[0].IsCommand.Should().BeFalse();
    }

    [Fact]
    public void Unknown_Action_Throws()
    {
        var (d, ctx, s, _, _, _) = NewRig();
        var act = () => d.Execute("transmit_warp_drive", ctx, s);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*unknown SDL action*transmit_warp_drive*");
    }
}
