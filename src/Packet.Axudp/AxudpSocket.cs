using System.Net;
using System.Net.Sockets;
using Packet.Ax25;

namespace Packet.Axudp;

/// <summary>
/// A bidirectional AXUDP endpoint. AXUDP is UDP encapsulation of raw AX.25
/// frames — the UDP payload is the AX.25 frame body (no opening / closing
/// HDLC flag, no FCS). Each peer maintains its own UDP socket and addresses
/// remotes by <see cref="IPEndPoint"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two on-the-wire forms exist, selected by the <c>includeFcs</c> flag on
/// <see cref="SendAsync"/>: the AX.25 body alone, or the body followed by the
/// 2-octet CRC-16/X.25 FCS (low byte first). Future work may add header-prefix
/// variants if real-world interop requires them.
/// </para>
/// <para>
/// FCS handling — verified against the reference peers, NOT assumed:
/// <list type="bullet">
///   <item><b>LinBPQ's BPQAXIP driver over UDP REQUIRES the FCS.</b> Source-
///   verified in <c>bpqaxip.c</c> (LinBPQ 6.0.25.23) and confirmed on the wire:
///   its UDP receive path computes the FCS over the whole datagram and drops
///   anything whose residue isn't <c>0xf0b8</c> ("BPQAXIP Invalid CRC"), and its
///   send path appends the 2-octet FCS. There is no per-MAP "no CRC" knob — the
///   FCS is unconditional on the UDP path. A peer talking to BPQAXIP/UDP must
///   therefore <c>includeFcs: true</c> and must tolerate the FCS on receive.</item>
///   <item><b>XRouter's AXUDP likewise requires the FCS</b> (it rejects FCS-less
///   bodies as "non-AXUDP").</item>
///   <item>The FCS-less form (<c>includeFcs: false</c>, the default) is the
///   minimal "raw body" variant — used for pdn↔pdn tunnels and any peer that
///   explicitly wants no FCS. It is NOT what the de-facto reference peers above
///   accept, so do not assume it for BPQ/XRouter interop.</item>
/// </list>
/// <see cref="SendAsync"/> writes the body, plus the FCS when asked;
/// <see cref="ReceiveAsync"/> returns the raw datagram bytes (a session-aware
/// consumer strips the FCS — see <c>AxudpKissModem</c>).
/// </para>
/// </remarks>
public sealed class AxudpSocket : IDisposable
{
    private readonly UdpClient udp;

    /// <summary>The local UDP port we're bound to.</summary>
    public int LocalPort { get; }

    /// <summary>
    /// Open an AXUDP endpoint bound to <paramref name="localPort"/> (0 picks
    /// any free ephemeral port). The endpoint listens on all interfaces.
    /// </summary>
    public AxudpSocket(int localPort = 0)
    {
        udp = new UdpClient(new IPEndPoint(IPAddress.Any, localPort));
        LocalPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }

    /// <summary>
    /// Send a serialised AX.25 frame to <paramref name="remote"/>.
    /// </summary>
    /// <param name="remote">The remote endpoint to send to.</param>
    /// <param name="frame">Frame to send.</param>
    /// <param name="includeFcs">
    /// If <c>true</c>, append the CRC-16-CCITT (X.25) FCS, low-byte first —
    /// required by LinBPQ's BPQAXIP driver over UDP (source-verified: it drops
    /// FCS-less datagrams as "Invalid CRC"), by XRouter's AXUDP listener, and by
    /// the AXIP-with-CRC variant. Use <c>true</c> for any of those reference
    /// peers. If <c>false</c> (the default), send the FCS-less "raw body" form —
    /// the minimal variant for a peer that explicitly wants no FCS (e.g. a
    /// pdn↔pdn tunnel); the de-facto reference peers above reject it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> SendAsync(IPEndPoint remote, Ax25Frame frame, bool includeFcs = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var bytes = includeFcs ? frame.ToBytesWithFcs() : frame.ToBytes();
        return await udp.SendAsync(bytes, remote, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Send raw bytes to <paramref name="remote"/>. Caller is responsible for
    /// constructing a well-formed AX.25 frame body — useful for replaying
    /// captures or forwarding unparsed frames.
    /// </summary>
    public async Task<int> SendRawAsync(IPEndPoint remote, ReadOnlyMemory<byte> rawFrame, CancellationToken cancellationToken = default)
    {
        return await udp.SendAsync(rawFrame, remote, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Wait for the next datagram. Returns the sender endpoint, the raw bytes,
    /// and an attempted decode (null if the bytes weren't a parseable AX.25
    /// frame).
    /// </summary>
    /// <remarks>
    /// The decode is best-effort at modulo-8: this transport layer has no
    /// session context, so it can't know whether the link is extended
    /// (modulo-128) — and an extended I/S frame's control-field width isn't
    /// derivable from the octets alone. The raw bytes are returned alongside,
    /// so a session-aware consumer must re-parse at the link's negotiated
    /// modulo (see <c>Ax25Frame.TryParse(…, extended, …)</c>) before trusting
    /// N(S)/N(R)/PID/info on an extended link. <see cref="AxudpReceiveResult.DecodedFrame"/>
    /// is suitable for monitor/identification (addresses + frame type, which are
    /// modulo-independent) without that second pass.
    /// </remarks>
    public async Task<AxudpReceiveResult> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var result = await udp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        var raw = result.Buffer;
        Ax25Frame? decoded = Ax25Frame.TryParse(raw, out var frame) ? frame : null;
        return new AxudpReceiveResult(result.RemoteEndPoint, raw, decoded);
    }

    /// <inheritdoc/>
    public void Dispose() => udp.Dispose();
}

/// <summary>
/// One received AXUDP datagram.
/// </summary>
/// <param name="From">The remote endpoint that sent the datagram.</param>
/// <param name="RawFrame">The raw UDP payload — the AX.25 frame bytes.</param>
/// <param name="DecodedFrame">
/// The parsed frame, or <c>null</c> if the bytes did not decode as a valid
/// AX.25 frame (callers may still want to inspect <see cref="RawFrame"/>).
/// </param>
public readonly record struct AxudpReceiveResult(IPEndPoint From, byte[] RawFrame, Ax25Frame? DecodedFrame);
