using Packet.Node.Core.Configuration;

namespace Packet.Node.Tests.Configuration;

/// <summary>
/// The NET/ROM routing back-compat resolver (<see cref="NetRomConfig.ResolveRouting"/>):
/// the new <c>routing</c> knob plus the legacy <c>connect</c>/<c>forward</c> bools map
/// onto a single 3-state role, with warnings for the lossy/contradictory cases. This is
/// the contract that keeps every deployed config (incl. the shipped default) behaving
/// identically after the connect+forward → routing collapse.
/// </summary>
public class NetRomRoutingResolverTests
{
    // ── Nothing set → None (the default) ───────────────────────────────
    [Fact]
    public void Unset_resolves_to_None_with_no_warnings()
    {
        var cfg = new NetRomConfig();
        var (routing, warnings) = cfg.ResolveRouting();
        routing.Should().Be(NetRomRouting.None);
        warnings.Should().BeEmpty();
        cfg.EffectiveRouting.Should().Be(NetRomRouting.None);
    }

    // ── Legacy bool combos (the deployed-config back-compat matrix) ─────
    // connect==true && forward!=false → Transit
    [Theory]
    [InlineData(true, true)]    // connect:true, forward:true  → full router
    [InlineData(true, null)]    // connect:true, forward absent → forward defaulted on → Transit
    public void Legacy_connect_with_forward_on_or_absent_resolves_to_Transit(bool connect, bool? forward)
    {
        var (routing, warnings) = new NetRomConfig { Connect = connect, Forward = forward }.ResolveRouting();
        routing.Should().Be(NetRomRouting.Transit);
        warnings.Should().BeEmpty();
    }

    // connect==true && forward==false → Endpoint
    [Fact]
    public void Legacy_connect_true_forward_false_resolves_to_Endpoint()
    {
        var (routing, warnings) = new NetRomConfig { Connect = true, Forward = false }.ResolveRouting();
        routing.Should().Be(NetRomRouting.Endpoint);
        warnings.Should().BeEmpty();
    }

    // connect!=true && forward!=true → None
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, null)]
    [InlineData(null, false)]
    public void Legacy_no_connect_and_no_forward_resolves_to_None(bool? connect, bool? forward)
    {
        var (routing, warnings) = new NetRomConfig { Connect = connect, Forward = forward }.ResolveRouting();
        routing.Should().Be(NetRomRouting.None);
        warnings.Should().BeEmpty();
    }

    // connect!=true && forward==true → None + warning (the always-inert combo)
    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Legacy_forward_true_without_connect_resolves_to_None_with_warning(bool? connect)
    {
        var (routing, warnings) = new NetRomConfig { Connect = connect, Forward = true }.ResolveRouting();
        routing.Should().Be(NetRomRouting.None,
            "forward without connect was always inert — it must not silently become a router");
        warnings.Should().ContainSingle()
            .Which.Should().Contain("forward").And.Contain("none");
    }

    // ── routing knob wins over legacy ──────────────────────────────────
    [Theory]
    [InlineData(NetRomRouting.None)]
    [InlineData(NetRomRouting.Endpoint)]
    [InlineData(NetRomRouting.Transit)]
    public void Explicit_routing_wins_with_no_legacy_keys_and_no_warnings(NetRomRouting mode)
    {
        var (routing, warnings) = new NetRomConfig { Routing = mode }.ResolveRouting();
        routing.Should().Be(mode);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Explicit_routing_wins_over_contradicting_legacy_keys_and_warns()
    {
        // routing:none beats a legacy connect:true,forward:true that would have been Transit.
        var (routing, warnings) = new NetRomConfig
        {
            Routing = NetRomRouting.None,
            Connect = true,
            Forward = true,
        }.ResolveRouting();

        routing.Should().Be(NetRomRouting.None, "the explicit routing knob wins");
        warnings.Should().ContainSingle()
            .Which.Should().Contain("ignored").And.Contain("routing");
    }

    [Fact]
    public void Explicit_routing_with_no_legacy_keys_does_not_warn()
    {
        // Only the presence of stale legacy keys triggers the "ignored" warning.
        new NetRomConfig { Routing = NetRomRouting.Transit }.ResolveRouting()
            .Warnings.Should().BeEmpty();
    }
}
