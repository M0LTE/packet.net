using System.Linq;
using Packet.Core;
using Packet.NetRom.Routing;
using Packet.NetRom.Wire;

namespace Packet.NetRom;

/// <summary>
/// The NET/ROM L3 <b>forwarding decision</b> — what a transit node does with a
/// datagram whose destination is <em>not</em> itself: drop it, or forward it (with a
/// decremented, capped TTL) to a next-hop neighbour. Pure (no I/O): the node host
/// feeds it the datagram, the neighbour it arrived from, this node's callsign, the
/// routing view, and the TTL cap; the host then performs the interlink send for a
/// <see cref="ForwardOutcome.ForwardTo"/> outcome.
/// </summary>
/// <remarks>
/// Mirrors the forward routine in the de-facto reference (LinBPQ <c>L4Code.c</c>):
/// decrement the hop limit and discard at zero; cap the TTL on everything sent;
/// drop a datagram that has looped back to its own origin; resolve the destination's
/// best route whose neighbour is not the one it just arrived from (so it is never
/// bounced straight back the way it came); otherwise forward. The caller has already
/// established the datagram is not addressed to this node (the "for us" check
/// terminates locally before forwarding is considered).
/// </remarks>
public static class NetRomForwarding
{
    /// <summary>What <see cref="Decide"/> determined should happen to a datagram.</summary>
    public enum ForwardOutcome
    {
        /// <summary>Forward it (with the rewritten header) to <see cref="ForwardDecision.NextHop"/>.</summary>
        ForwardTo,

        /// <summary>Drop: the hop limit reached zero.</summary>
        DropTtlExpired,

        /// <summary>Drop: the datagram's origin is this node — it has looped back.</summary>
        DropLooped,

        /// <summary>Drop: no onward route to the destination (excluding the way it came).</summary>
        DropNoRoute,
    }

    /// <summary>The outcome of a forwarding decision. When
    /// <see cref="ForwardOutcome.ForwardTo"/>, <see cref="Packet"/> carries the
    /// rewritten (TTL-decremented) datagram to send to <see cref="NextHop"/>.</summary>
    public readonly record struct ForwardDecision(ForwardOutcome Outcome, NetRomPacket Packet, Callsign NextHop)
    {
        /// <summary>True if the datagram should be forwarded.</summary>
        public bool ShouldForward => Outcome == ForwardOutcome.ForwardTo;
    }

    /// <summary>
    /// Decide what to do with a transit datagram. The caller has already confirmed
    /// <paramref name="packet"/>'s destination is not <paramref name="nodeCall"/>.
    /// </summary>
    /// <param name="packet">The received datagram.</param>
    /// <param name="receivedFrom">The neighbour the datagram arrived from (so it is
    /// not bounced straight back to it).</param>
    /// <param name="nodeCall">This node's callsign (for the loop guard).</param>
    /// <param name="routing">The current routing view.</param>
    /// <param name="maxTimeToLive">The TTL cap applied to everything forwarded (the
    /// node's configured initial TTL — BPQ's <c>L3LIVES</c>).</param>
    public static ForwardDecision Decide(
        NetRomPacket packet,
        Callsign receivedFrom,
        Callsign nodeCall,
        NetRomRoutingSnapshot routing,
        byte maxTimeToLive)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(routing);

        // 1. Decrement the hop limit; a datagram that arrives at TTL 1 (or 0) is at
        //    the end of its life and must not be forwarded.
        var network = packet.Network.Decremented();
        if (network.TimeToLive == 0)
        {
            return new ForwardDecision(ForwardOutcome.DropTtlExpired, packet, default);
        }

        // 2. Cap the TTL on everything sent, so a buggy/hostile peer can't make a
        //    frame circulate longer than this node's own initial TTL.
        if (network.TimeToLive > maxTimeToLive)
        {
            network = network with { TimeToLive = maxTimeToLive };
        }

        // 3. Loop guard: a datagram whose origin is this node has come back to its
        //    start — forwarding it again just loops.
        if (network.Origin.Equals(nodeCall))
        {
            return new ForwardDecision(ForwardOutcome.DropLooped, packet, default);
        }

        // 4. Next hop: the destination's best route (Routes is best-first) whose
        //    neighbour is not the one it arrived from.
        var resolved = routing.Destinations.FirstOrDefault(d => d.Destination.Equals(network.Destination));
        Callsign? nextHop = null;
        if (resolved is not null)
        {
            foreach (var route in resolved.Routes)
            {
                if (!route.Neighbour.Equals(receivedFrom))
                {
                    nextHop = route.Neighbour;
                    break;
                }
            }
        }

        if (nextHop is null)
        {
            return new ForwardDecision(ForwardOutcome.DropNoRoute, packet, default);
        }

        return new ForwardDecision(ForwardOutcome.ForwardTo, packet with { Network = network }, nextHop.Value);
    }
}
