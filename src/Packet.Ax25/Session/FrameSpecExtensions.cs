using Packet.Core;

namespace Packet.Ax25.Session;

/// <summary>
/// Convert dispatcher-emitted frame-spec records into <see cref="Ax25Frame"/>
/// instances ready for wire serialisation via <see cref="Ax25Frame.ToBytes"/>
/// or <see cref="Ax25Frame.ToBytesWithFcs"/>.
/// </summary>
/// <remarks>
/// <para>
/// The frame specs (<see cref="SupervisoryFrameSpec"/>, <see cref="UFrameSpec"/>,
/// <see cref="UiFrameSpec"/>, <see cref="IFrameSpec"/>) describe what the
/// dispatcher wants to send — frame type, control bits, payload — without
/// any addressing. Addressing is per-session: it comes from the
/// <see cref="Ax25SessionContext"/>'s <c>Local</c> / <c>Remote</c> /
/// <c>Digipeaters</c> fields. The conversion lives here rather than on the
/// spec records so the spec types stay address-agnostic (a spec describes
/// "send a DM" the same way regardless of who it's going to).
/// </para>
/// <para>
/// Source is always <see cref="Ax25SessionContext.Local"/> and destination
/// is always <see cref="Ax25SessionContext.Remote"/> — the dispatcher emits
/// frames going outbound from us to the peer. The digipeater chain comes
/// from the context too; in production it's set at session construction
/// time from the link-level configuration.
/// </para>
/// </remarks>
public static class FrameSpecExtensions
{
    /// <summary>Build an <see cref="Ax25Frame"/> for an outgoing supervisory frame using session addressing.</summary>
    public static Ax25Frame ToAx25Frame(this SupervisoryFrameSpec spec, Ax25SessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return spec.Type switch
        {
            SupervisoryFrameType.Rr   => Ax25Frame.Rr  (context.Remote, context.Local, spec.Nr, spec.IsCommand, spec.PfBit, context.Digipeaters),
            SupervisoryFrameType.Rnr  => Ax25Frame.Rnr (context.Remote, context.Local, spec.Nr, spec.IsCommand, spec.PfBit, context.Digipeaters),
            SupervisoryFrameType.Rej  => Ax25Frame.Rej (context.Remote, context.Local, spec.Nr, spec.IsCommand, spec.PfBit, context.Digipeaters),
            SupervisoryFrameType.Srej => Ax25Frame.Srej(context.Remote, context.Local, spec.Nr, spec.IsCommand, spec.PfBit, context.Digipeaters),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), $"unknown supervisory frame type '{spec.Type}'"),
        };
    }

    /// <summary>Build an <see cref="Ax25Frame"/> for an outgoing unnumbered frame using session addressing.</summary>
    public static Ax25Frame ToAx25Frame(this UFrameSpec spec, Ax25SessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return spec.Type switch
        {
            UFrameType.Sabm  => Ax25Frame.Sabm (context.Remote, context.Local, spec.PfBit, context.Digipeaters),
            UFrameType.Sabme => Ax25Frame.Sabme(context.Remote, context.Local, spec.PfBit, context.Digipeaters),
            UFrameType.Disc  => Ax25Frame.Disc (context.Remote, context.Local, spec.PfBit, context.Digipeaters),
            UFrameType.Ua    => Ax25Frame.Ua   (context.Remote, context.Local, spec.PfBit, context.Digipeaters),
            UFrameType.Dm    => Ax25Frame.Dm   (context.Remote, context.Local, spec.PfBit, context.Digipeaters),
            UFrameType.Frmr  => Ax25Frame.Frmr (context.Remote, context.Local, info: ReadOnlySpan<byte>.Empty, spec.PfBit, context.Digipeaters),
            UFrameType.Xid   => Ax25Frame.Xid  (context.Remote, context.Local, info: ReadOnlySpan<byte>.Empty, spec.IsCommand, spec.PfBit, context.Digipeaters),
            UFrameType.Test  => Ax25Frame.Test (context.Remote, context.Local, info: ReadOnlySpan<byte>.Empty, spec.IsCommand, spec.PfBit, context.Digipeaters),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), $"unknown unnumbered frame type '{spec.Type}'"),
        };
    }

    /// <summary>Build an <see cref="Ax25Frame"/> for an outgoing UI frame using session addressing.</summary>
    public static Ax25Frame ToAx25Frame(this UiFrameSpec spec, Ax25SessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Ax25Frame.Ui(
            destination: context.Remote,
            source:      context.Local,
            info:        spec.Info.Span,
            pid:         spec.Pid,
            isCommand:   spec.IsCommand,
            pollFinal:   spec.PfBit,
            digipeaters: context.Digipeaters);
    }

    /// <summary>Build an <see cref="Ax25Frame"/> for an outgoing I-frame using session addressing.</summary>
    public static Ax25Frame ToAx25Frame(this IFrameSpec spec, Ax25SessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Ax25Frame.I(
            destination: context.Remote,
            source:      context.Local,
            nr:          spec.Nr,
            ns:          spec.Ns,
            info:        spec.Info.Span,
            pid:         spec.Pid,
            pollBit:     spec.PBit,
            digipeaters: context.Digipeaters);
    }
}
