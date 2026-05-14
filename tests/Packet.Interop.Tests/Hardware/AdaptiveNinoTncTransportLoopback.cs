using System.Text;
using Packet.Ax25;
using Packet.Core;
using Packet.Kiss;
using Packet.Kiss.Adaptive;
using Packet.Kiss.NinoTnc;
using Xunit;

namespace Packet.Interop.Tests.Hardware;

[Trait("Category", "HardwareLoop")]
public class AdaptiveNinoTncTransportLoopback
{
    private const byte LoopbackMode = 6;

    [SkippableFact]
    public async Task TxDelay_Hill_Climb_Reduces_TxDelay_Over_A_Run_Of_Successes()
    {
        var ports = SelectTwoPorts();
        await using var a = NinoTncSerialPort.Open(ports[0]);
        await using var b = NinoTncSerialPort.Open(ports[1]);
        await a.SetModeAsync(LoopbackMode);
        await b.SetModeAsync(LoopbackMode);
        await Task.Delay(500);

        // Aggressive tuning so 20 frames give us a clearly observable walk-down.
        var estimator = new TxDelayHillClimbEstimator(initialTxDelay: 40)
        {
            SuccessesPerStepDown = 3,
            StepUnits = 2,
            MinTxDelay = 5,
            LossPenaltyUnits = 10,
        };
        await using var transport = new AdaptiveNinoTncTransport(a, estimator);

        const string peer = "BB-2";
        int rxCount = 0;
        b.FrameReceived += (_, frame) =>
        {
            if (frame.Command == KissCommand.Data && Ax25Frame.TryParse(frame.Payload, out var _))
            {
                Interlocked.Increment(ref rxCount);
            }
        };

        for (int i = 0; i < 20; i++)
        {
            var ax25 = Ax25Frame.Ui(
                destination: new Callsign("BB", 2),
                source: new Callsign("AA", 1),
                info: Encoding.ASCII.GetBytes($"ADAPT-{i:00}"));
            await transport.SendAsync(peer, ax25.ToBytes(), TimeSpan.FromSeconds(15));
        }

        var finalTxDelay = estimator.CurrentTxDelayFor(peer);
        finalTxDelay.Should().NotBeNull();
        finalTxDelay!.Value.Should().BeLessThan((byte)40,
            "20 successful first-try ACKs should have walked TXDELAY down from the initial 40");
        rxCount.Should().BeGreaterThanOrEqualTo(20,
            "every adaptive send should have produced at least one inbound frame on the partner");
    }

    private static List<string> SelectTwoPorts()
    {
        var candidates = NinoTncPortDiscovery.EnumerateCandidates();
        Skip.If(
            candidates.Count < 2,
            $"Hardware-loop test: expected ≥2 NinoTNC-class serial devices, " +
            $"found {candidates.Count}. " +
            $"Set {NinoTncPortDiscovery.PortsEnvVar}=\"<porta>,<portb>\" to pick explicitly.");
        return candidates.Take(2).Select(c => c.PortName).ToList();
    }
}
