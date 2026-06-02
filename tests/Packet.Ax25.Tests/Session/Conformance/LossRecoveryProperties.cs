using FsCheck;
using FsCheck.Xunit;
using Xunit;
using Packet.Ax25;
using Packet.Ax25.Session;
using Packet.Ax25.Tests.Session;   // GetIFrameNs extension

namespace Packet.Ax25.Tests.Session.Conformance;

/// <summary>
/// Phase A1 — adversarial generative testing over the conformance harness.
/// FsCheck generates loss patterns; the invariant oracle judges. A failure is
/// a shrinkable counterexample, automatically triaged engine-vs-figure by the
/// Strict-vs-Default companion (future A2). This first property fuzzes the
/// #231-class: a connected-mode transfer with a single dropped I-frame must
/// always recover (everything delivered in order, windows empty) once the
/// channel clears — for both REJ go-back-N and SREJ selective recovery.
/// </summary>
public class LossRecoveryProperties
{
    [Property(MaxTest = 300)]
    public bool A_single_dropped_iframe_always_recovers(int seedN, int seedDrop, bool srej)
    {
        int n       = 1 + Mod(seedN, 6);   // 1..6 I-frames
        int dropPos = Mod(seedDrop, n);    // drop the frame with N(s)=dropPos, once
        int k       = Math.Max(4, n);

        var h = TwoStationHarness.Build(srej: srej, k: k);
        h.Connect();

        var dropped = false;
        h.Link.Drop = f =>
        {
            if (dropped) return false;
            if (!f.Source.Callsign.Equals(h.A.Context.Local)) return false;
            if (Ax25FrameClassifier.Classify(f) is not IFrameReceived) return false;
            if (f.GetIFrameNs(8) != dropPos) return false;
            dropped = true;
            return true;
        };

        for (byte i = 0; i < n; i++) h.Submit(h.A, i);

        // Channel is clean once the single drop is consumed; drive T1 recovery
        // until the link converges (bounded — non-convergence is the bug we hunt).
        for (int r = 0; r < 40 && !Converged(h); r++) h.AdvanceT1();

        h.AssertConverged();  // throws InvariantViolationException → shrinkable counterexample
        return true;
    }

    [Property(MaxTest = 400)]
    public bool A_finite_bidirectional_loss_burst_recovers_with_rej(int seedN, int seedBudget, int seedPattern)
    {
        int n      = 1 + Mod(seedN, 6);        // 1..6 I-frames
        int budget = Mod(seedBudget, n + 1);   // 0..n total drops — finite, so the channel always clears
        int k      = Math.Max(4, n);
        var rng    = new Random(seedPattern);

        // REJ (go-back-N) recovery, fuzzed across budgets. SREJ multi-frame
        // recovery is still blocked by a third defect — #246: figc4.4 SREJs the
        // just-arrived out-of-sequence frame (N(r):=N(s)) instead of the gap, so a
        // multi-frame loss can livelock. That only became visible once #242 (the
        // window guard) and #241 (the SRT/T1V overflow) were fixed. So this
        // generative bidirectional sweep stays REJ-only; flip to a fuzzed `srej`
        // parameter once #246 lands. N2 generous so the link doesn't give up before
        // the finite loss clears.
        var h = TwoStationHarness.Build(srej: false, k: k, n2: 40);
        h.Connect();

        // Drop up to `budget` frames in EITHER direction (lost I-frames, acks,
        // SREJs, retransmits), then the channel is clean. The finite budget is
        // the "disruption prefix"; recovery must complete on the clean tail.
        int dropsLeft = budget;
        h.Link.Drop = _ =>
        {
            if (dropsLeft > 0 && rng.NextDouble() < 0.5) { dropsLeft--; return true; }
            return false;
        };

        for (byte i = 0; i < n; i++) h.Submit(h.A, i);
        for (int r = 0; r < 80 && !Converged(h); r++) h.AdvanceT1();

        h.AssertConverged();
        return true;
    }

    // Regression for the ax25spec#40 SREJ livelock (m0lte/packet.net#242). A
    // multi-frame bidirectional SREJ burst like this used to spin to the pump's
    // 256-round bound: B SREJ'd out-of-window duplicates (figc4.4 has no
    // receive-window guard), A re-sent, the re-send was again out-of-window,
    // repeat forever. With Ax25Spec40DiscardOutOfWindowIFrames on (default), B
    // discards out-of-window frames instead of SREJ'ing them, so this moderate
    // selective-reject burst converges. (#242 regression. A heavier burst is still
    // blocked by #246 — see the skipped Srej_heavy_bidirectional_loss_burst_recovers
    // below — so keep this budget moderate.)
    [Fact]
    public void Srej_bidirectional_loss_burst_recovers_with_window_guard()
    {
        var h = TwoStationHarness.Build(srej: true, k: 6, n2: 40);
        h.Connect();
        var rng = new Random(2);
        var dropsLeft = 2;
        h.Link.Drop = _ => { if (dropsLeft > 0 && rng.NextDouble() < 0.5) { dropsLeft--; return true; } return false; };
        for (byte i = 0; i < 6; i++) h.Submit(h.A, i);
        for (int r = 0; r < 60 && !Converged(h); r++) h.AdvanceT1();
        h.AssertConverged();
    }

    // The two SRT/T1V blockers under this heavy SREJ burst are now fixed: the
    // ax25spec#40 duplicate-SREJ livelock (window guard / #242) and the
    // ax25spec#41 SRT overflow (Karn guard / #241). What remains is #246 — the
    // figc4.4 multi-frame SREJ path requests the just-arrived frame (N(r):=N(s))
    // instead of the gap, so a heavy burst still livelocks (B SREJs a frame it
    // holds; the real gap is never re-requested). Un-skip when #246 lands; this
    // becomes its convergence regression.
    [Fact(Skip = "m0lte/packet.net#246: figc4.4 multi-frame SREJ requests the just-arrived frame, not the gap → heavy SREJ recovery livelocks. Overflow (#241) and duplicate-SREJ livelock (#242) are fixed; this is the remaining third defect.")]
    public void Srej_heavy_bidirectional_loss_burst_recovers()
    {
        var h = TwoStationHarness.Build(srej: true, k: 6, n2: 40);
        h.Connect();
        var rng = new Random(2);
        var dropsLeft = 5;
        h.Link.Drop = _ => { if (dropsLeft > 0 && rng.NextDouble() < 0.5) { dropsLeft--; return true; } return false; };
        for (byte i = 0; i < 6; i++) h.Submit(h.A, i);
        for (int r = 0; r < 60 && !Converged(h); r++) h.AdvanceT1();
        h.AssertConverged();
    }

    private static bool Converged(TwoStationHarness h) =>
        h.A.Context.VS == h.A.Context.VA &&
        h.B.Context.VS == h.B.Context.VA &&
        h.B.Delivered.Count == h.A.Submitted.Count &&
        h.A.Delivered.Count == h.B.Submitted.Count;

    /// <summary>Non-negative modulo, overflow-safe for int.MinValue.</summary>
    private static int Mod(int v, int m) => (int)(((long)v % m + m) % m);
}
