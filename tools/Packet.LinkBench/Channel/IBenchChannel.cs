using System.Net;
using Packet.Kiss;
using Packet.Node.Core.Transports;

namespace Packet.LinkBench.Channel;

/// <summary>
/// A pluggable channel joining the two bench engines (link-bench plan §3): each
/// engine talks to its own <see cref="IKissModem"/>; the channel model joins
/// them. Implementations: <see cref="InProcChannel"/> (primary, deterministic),
/// <see cref="AxudpChannel"/> (lossless cross-check, no ackmode by nature),
/// <see cref="NetSimChannel"/> (rung 2 — real AFSK over net-sim).
/// </summary>
internal interface IBenchChannel : IAsyncDisposable
{
    IKissModem EndpointA { get; }
    IKissModem EndpointB { get; }

    /// <summary>Whether <see cref="IKissModem.SendFrameWithAckAsync"/> works here.
    /// AXUDP is a tunnel — no TNC, no TX-complete echo — so it cannot.</summary>
    bool SupportsAckMode { get; }
}

/// <summary>
/// Two <see cref="AxudpKissModem"/>s on UDP loopback — real sockets, real
/// async/serialisation, full-duplex, lossless. Tom's "lossless AXUDP" baseline:
/// confirms an in-proc result isn't an artifact of the in-proc model.
/// </summary>
internal sealed class AxudpChannel : IBenchChannel
{
    private readonly AxudpKissModem a;
    private readonly AxudpKissModem b;

    public AxudpChannel(int portA, int portB)
    {
        a = new AxudpKissModem(new IPEndPoint(IPAddress.Loopback, portB), localPort: portA);
        b = new AxudpKissModem(new IPEndPoint(IPAddress.Loopback, portA), localPort: portB);
    }

    public IKissModem EndpointA => a;
    public IKissModem EndpointB => b;
    public bool SupportsAckMode => false;

    public async ValueTask DisposeAsync()
    {
        await a.DisposeAsync().ConfigureAwait(false);
        await b.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Two KISS-TCP clients into net-sim ports (rung 2): net-sim provides the real
/// channel (AFSK, half-duplex shared medium, loss knob) and — on the pinned
/// ackmode-capable image (plan §7) — the Samoyed modem's 0x0C echo.
/// </summary>
internal sealed class NetSimChannel : IBenchChannel
{
    private NetSimChannel(KissTcpClient a, KissTcpClient b)
    {
        EndpointA = a;
        EndpointB = b;
    }

    public IKissModem EndpointA { get; }
    public IKissModem EndpointB { get; }
    public bool SupportsAckMode => true;

    public static async Task<NetSimChannel> ConnectAsync(
        (string Host, int Port) a, (string Host, int Port) b, CancellationToken ct)
    {
        var clientA = await KissTcpClient.ConnectAsync(a.Host, a.Port, ct).ConfigureAwait(false);
        try
        {
            var clientB = await KissTcpClient.ConnectAsync(b.Host, b.Port, ct).ConfigureAwait(false);
            return new NetSimChannel(clientA, clientB);
        }
        catch
        {
            await clientA.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ((KissTcpClient)EndpointA).DisposeAsync().ConfigureAwait(false);
        await ((KissTcpClient)EndpointB).DisposeAsync().ConfigureAwait(false);
    }
}
