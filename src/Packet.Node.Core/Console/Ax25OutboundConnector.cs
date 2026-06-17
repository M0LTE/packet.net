using Packet.Ax25.Session;
using Packet.Core;
using Packet.Node.Core.Capabilities;

namespace Packet.Node.Core.Console;

/// <summary>
/// An <see cref="IOutboundConnector"/> that dials out on one
/// <see cref="Ax25Listener"/> — the slice-1 same-port connect-out. The console's
/// <c>Connect</c> command uses it to open an outbound session and wrap it as a
/// <see cref="Ax25NodeConnection"/> to relay against the inbound user.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Ax25Listener.ConnectAsync"/> raises <c>SessionAccepted</c> on
/// success — the SAME event the node uses to start an inbound console session.
/// Without coordination, dialling OUT to a station would also start a node
/// console <em>against that station</em> (spewing our prompt at it). The
/// optional <paramref name="claim"/> lets the owner (the port supervisor) mark
/// the dialled remote as outbound for the duration of the connect so its
/// <c>SessionAccepted</c> handler skips it; the claim is released once the
/// session is established (or the connect fails).
/// </para>
/// </remarks>
public sealed class Ax25OutboundConnector : IOutboundConnector
{
    private readonly Ax25Listener listener;
    private readonly Func<Callsign, IDisposable>? claim;
    private readonly Callsign? localOverride;
    // The per-peer capability cache. Null ⇒ today's behaviour exactly (dial via the
    // listener's PreferExtendedConnect default + its pre-connect-XID default, record
    // nothing). Non-null ⇒ the dial consults PlanDial to pick the version + XID probe
    // and records the OUTCOME of a RETURNED dial (never on a throw — a throw is no link
    // of either version, hence no capability signal).
    private readonly PeerCapabilityCache? cache;

    public Ax25OutboundConnector(
        string portId,
        Ax25Listener listener,
        Func<Callsign, IDisposable>? claim = null,
        Callsign? localOverride = null,
        PeerCapabilityCache? cache = null)
    {
        PortId = portId ?? throw new ArgumentNullException(nameof(portId));
        this.listener = listener ?? throw new ArgumentNullException(nameof(listener));
        this.claim = claim;
        // Originate from an application callsign instead of the port's own (the RHPv2
        // server's open.local) — multi-callsign origination; null = the listener's MyCall.
        this.localOverride = localOverride;
        this.cache = cache;
    }

    /// <inheritdoc/>
    public string PortId { get; }

    /// <inheritdoc/>
    public async Task<INodeConnection> ConnectAsync(Callsign target, CancellationToken cancellationToken = default)
    {
        // Claim the remote as outbound so the supervisor's SessionAccepted handler
        // doesn't start a console session against it. Held across ConnectAsync
        // because the listener fires SessionAccepted synchronously within it.
        var ticket = claim?.Invoke(target);
        try
        {
            var local = localOverride ?? listener.MyCall;

            // No cache ⇒ today's exact call: the no-extended-arg overload follows the
            // listener's PreferExtendedConnect + PreConnectXidNegotiatesSrej defaults,
            // and we record nothing. Preserves every existing connector unchanged.
            if (cache is null)
            {
                var sessionNoCache = localOverride is { } lo
                    ? await listener.ConnectAsync(target, lo, cancellationToken).ConfigureAwait(false)
                    : await listener.ConnectAsync(target, cancellationToken).ConfigureAwait(false);
                return new Ax25NodeConnection(listener, sessionNoCache);
            }

            // Cache present ⇒ consult the plan. PlanDial's miss/stale default for a user
            // CONNECT is the optimistic SABME + (moot) no-XID; a learned answer overrides
            // it. (The no-cache path above is the one that defers to the listener's own
            // PreferExtendedConnect / PreConnectXidNegotiatesSrej defaults.)
            var plan = cache.PlanDial(PortId, target.ToString(), PeerDialPolicy.UserConnect);

            var session = await listener
                .ConnectAsync(target, local, plan.Extended, plan.PreConnectXid, cancellationToken)
                .ConfigureAwait(false);

            // Record the OUTCOME of this RETURNED dial (plan-aware: pass what we dialled +
            // what the resulting link observed; the cache decides which dimension to learn).
            // A throw above never reaches here — no link of either version means no signal.
            cache.RecordOutcome(
                PortId, target.ToString(),
                dialedExtended: plan.Extended, observedIsExtended: session.Context.IsExtended,
                dialedPreConnectXid: plan.PreConnectXid, observedSrejEnabled: session.Context.SrejEnabled);

            return new Ax25NodeConnection(listener, session);
        }
        finally
        {
            ticket?.Dispose();
        }
    }
}
