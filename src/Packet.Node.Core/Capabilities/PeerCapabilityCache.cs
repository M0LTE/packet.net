using System.Collections.Concurrent;

namespace Packet.Node.Core.Capabilities;

/// <summary>
/// Remembers, per neighbour, whether it supports v2.2/SABME (<see cref="PeerCapabilityRecord.SupportsExtended"/>)
/// and whether it answers a pre-session XID (<see cref="PeerCapabilityRecord.SupportsSrejViaXid"/>), so a
/// dial can skip probes a known non-answerer would only stall on, and re-probe a negative after ~30 days.
/// </summary>
/// <remarks>
/// <para>
/// The dial decision is <see cref="PlanDial"/>; the post-dial learning is
/// <see cref="RecordOutcome"/>. A hot <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by the
/// (port, peer) pair is hydrated from <see cref="IPeerCapabilityStore.All"/> on construction and written
/// through on every update, so reads (the dial hot path) never touch the database.
/// </para>
/// <para>
/// The store is <b>optional</b> (mirroring <see cref="NetRom.NetRomService"/>): a null store ⇒ in-memory
/// only — the cache still works for the run, it just doesn't survive a restart. That keeps tests and
/// embedders that don't supply a <c>pdn.db</c> unaffected.
/// </para>
/// </remarks>
public sealed class PeerCapabilityCache
{
    /// <summary>A learned negative is re-probed after this long, in case the peer (or its firmware) changed.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    private readonly IPeerCapabilityStore? store;
    private readonly TimeProvider time;
    private readonly ConcurrentDictionary<(string PortId, string Peer), PeerCapabilityRecord> hot = new();

    /// <summary>Build the cache over an optional <paramref name="store"/> (null ⇒ in-memory only) and an
    /// optional <paramref name="time"/> source (default <see cref="TimeProvider.System"/>). Hydrates the
    /// hot dictionary from the store on construction.</summary>
    public PeerCapabilityCache(IPeerCapabilityStore? store = null, TimeProvider? time = null)
    {
        this.store = store;
        this.time = time ?? TimeProvider.System;

        if (store is not null)
        {
            foreach (var rec in store.All())
            {
                hot[(rec.PortId, rec.Peer)] = rec;
            }
        }
    }

    /// <summary>
    /// Decide how to dial <paramref name="peer"/> on <paramref name="portId"/>. A miss or a stale record
    /// falls back to the optimistic <paramref name="policy"/> default; a fresh learned positive is honoured
    /// (offer SABME); a fresh learned negative is skipped (mod-8, and skip the pre-connect XID if the peer
    /// is a known non-answerer).
    /// </summary>
    public PeerDialPlan PlanDial(string portId, string peer, PeerDialPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(portId);
        ArgumentNullException.ThrowIfNull(peer);

        var rec = Lookup(portId, peer);

        // Extended: a fresh learned answer wins; otherwise the policy's optimistic default
        // (UserConnect offers SABME, Interlink stays mod-8 until proven extended).
        bool extended = Fresh(rec, rec?.SupportsExtended)
            ? rec!.SupportsExtended!.Value
            : policy == PeerDialPolicy.UserConnect;

        // Pre-connect XID: moot on the extended path (XID negotiation rides the SABME setup). Off the
        // extended path, send the XID unless we have freshly learned this peer does NOT answer it.
        bool preConnectXid = !extended
            && !(Fresh(rec, rec?.SupportsSrejViaXid) && rec!.SupportsSrejViaXid == false);

        return new PeerDialPlan(extended, preConnectXid);
    }

    /// <summary>
    /// Record what a returned dial observed. <b>Plan-aware</b>: a dimension is only updated when the dial
    /// actually probed it — a mod-8 dial proves nothing about extended capability, so it leaves
    /// <see cref="PeerCapabilityRecord.SupportsExtended"/> untouched; a dial that sent no pre-connect XID
    /// leaves <see cref="PeerCapabilityRecord.SupportsSrejViaXid"/> untouched. The unprobed dimension is
    /// preserved from the existing record.
    /// </summary>
    /// <param name="portId">The port the dial used.</param>
    /// <param name="peer">The neighbour dialled.</param>
    /// <param name="dialedExtended">Whether the dial offered SABME (extended setup).</param>
    /// <param name="observedIsExtended">Whether the resulting link is extended (true = capable, false =
    /// peer refused / degraded to mod-8). Only meaningful when <paramref name="dialedExtended"/>.</param>
    /// <param name="dialedPreConnectXid">Whether the dial sent a pre-connect XID.</param>
    /// <param name="observedSrejEnabled">Whether the XID exchange enabled SREJ. Only meaningful when
    /// <paramref name="dialedPreConnectXid"/>.</param>
    public void RecordOutcome(
        string portId,
        string peer,
        bool dialedExtended,
        bool observedIsExtended,
        bool dialedPreConnectXid,
        bool observedSrejEnabled)
    {
        ArgumentNullException.ThrowIfNull(portId);
        ArgumentNullException.ThrowIfNull(peer);

        var now = time.GetUtcNow();
        var existing = Lookup(portId, peer);

        // Only learn a dimension we actually probed; otherwise carry the prior value forward.
        bool? supportsExtended = dialedExtended ? observedIsExtended : existing?.SupportsExtended;
        bool? supportsSrejViaXid = dialedPreConnectXid ? observedSrejEnabled : existing?.SupportsSrejViaXid;

        // LastRefused stamps an extended degrade (we offered SABME, peer came back mod-8); else carry forward.
        DateTimeOffset? lastRefused = (dialedExtended && !observedIsExtended) ? now : existing?.LastRefused;

        var updated = new PeerCapabilityRecord(
            portId, peer, supportsExtended, supportsSrejViaXid, now, lastRefused);

        hot[(portId, peer)] = updated;
        store?.Upsert(updated);
    }

    /// <summary>Every cached record (operator surface for later phases).</summary>
    public IReadOnlyList<PeerCapabilityRecord> All() => hot.Values.ToList();

    /// <summary>Forget one (port, peer) — clears the store and the hot dictionary. Returns whether the hot
    /// entry was present (the store delete is best-effort).</summary>
    public bool Forget(string portId, string peer)
    {
        ArgumentNullException.ThrowIfNull(portId);
        ArgumentNullException.ThrowIfNull(peer);

        store?.Clear(portId, peer);
        return hot.TryRemove((portId, peer), out _);
    }

    private PeerCapabilityRecord? Lookup(string portId, string peer) =>
        hot.TryGetValue((portId, peer), out var rec) ? rec : null;

    // A learned dimension is fresh when the record exists, that dimension has a value, and the record was
    // probed within the staleness window. A null dimension (never probed) is never "fresh".
    private bool Fresh(PeerCapabilityRecord? rec, bool? dimension) =>
        rec is not null && dimension.HasValue && (time.GetUtcNow() - rec.LastProbed) < StaleAfter;
}
