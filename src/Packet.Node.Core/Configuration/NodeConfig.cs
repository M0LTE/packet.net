using Packet.NetRom;
using Packet.NetRom.Wire;
namespace Packet.Node.Core.Configuration;

/// <summary>
/// The node's complete configuration, as a format-agnostic immutable record
/// tree. This is the <b>stable interface</b> every consumer
/// (<see cref="Hosting.NodeHostedService"/>, the console, the port supervisor)
/// depends on — it deliberately knows nothing about YAML, SQLite, or whatever
/// future store the config lives in. The <see cref="IConfigProvider"/> seam
/// produces <see cref="NodeConfig"/> instances; everything downstream reads
/// only this shape.
/// </summary>
/// <remarks>
/// Slice 1 loads this from a YAML file (<see cref="FileConfigProvider"/>); a
/// later slice stores the same YAML in a <c>config.db</c> column behind the
/// same seam. Nothing here couples to the serialisation format.
/// </remarks>
public sealed record NodeConfig
{
    /// <summary>Schema version of the persisted config. Bumped when the shape
    /// changes incompatibly; lets a future loader migrate older blobs.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Station identity — the callsign every <see cref="Ports"/> entry
    /// listens as, plus optional human-facing metadata.</summary>
    public required Identity Identity { get; init; }

    /// <summary>The configured AX.25 ports. May be empty — a node with no ports
    /// is a legal idle node (it still answers telnet + <c>/healthz</c>).</summary>
    public IReadOnlyList<PortConfig> Ports { get; init; } = [];

    /// <summary>Operator-facing service text (banner, prompt) — hot-reloadable
    /// by reference swap.</summary>
    public ServicesConfig Services { get; init; } = new();

    /// <summary>Management surfaces: the local telnet console and the (slice-1
    /// inert) web server bind.</summary>
    public ManagementConfig Management { get; init; } = new();

    /// <summary>NET/ROM awareness (read-only): hear NODES broadcasts and build a
    /// routing table. Pure consumer — never transmits. See <see cref="NetRomConfig"/>.</summary>
    public NetRomConfig NetRom { get; init; } = new();

    /// <summary>The system-default ID beacon — a periodic connectionless AX.25 UI
    /// frame sent per port to announce the node's presence. Default-OFF
    /// (<see cref="BeaconConfig.Enabled"/> defaults <c>false</c>): a node that never
    /// beaconed keeps not beaconing. A port may override it with
    /// <see cref="PortConfig.Beacon"/>. See <see cref="BeaconConfig"/>.</summary>
    public BeaconConfig Beacon { get; init; } = new();
}

/// <summary>
/// Station identity. <see cref="Callsign"/> is held as a <see cref="string"/>
/// deliberately: <c>Packet.Core.Callsign</c> is a <c>readonly struct</c> that
/// will not bind cleanly as a nested config object, so the raw text is carried
/// here and parsed (via <c>Callsign.TryParse</c>) in validation.
/// </summary>
public sealed record Identity
{
    /// <summary>The node's callsign as text (e.g. <c>"M0LTE-1"</c>). Parsed +
    /// validated by <see cref="NodeConfigValidator"/>; never bound as a struct.</summary>
    public required string Callsign { get; init; }

    /// <summary>Optional human-facing alias / node name (e.g. <c>"LONDON"</c>).</summary>
    public string? Alias { get; init; }

    /// <summary>Optional Maidenhead grid locator (e.g. <c>"IO91wm"</c>). Free-form
    /// in slice 1 — not validated as a grid square yet.</summary>
    public string? Grid { get; init; }
}

/// <summary>
/// One AX.25 port — a single radio channel reached through one KISS transport,
/// hosting exactly one <c>Ax25Listener</c>.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is the <b>stable reconcile key</b>: the hot-reload delta
/// matches old and new ports by <see cref="Id"/> to decide added / removed /
/// changed. Renaming a port's <see cref="Id"/> therefore reads as "remove the
/// old, add the new" — a full restart of that port. Keep it stable across edits.
/// </remarks>
public sealed record PortConfig
{
    /// <summary>Stable, operator-chosen identifier for this port (the reconcile
    /// key — see the type remarks). Must be unique within
    /// <see cref="NodeConfig.Ports"/>.</summary>
    public required string Id { get; init; }

    /// <summary>Whether the port should be brought up. A disabled port is
    /// retained in config but torn down at runtime.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How to reach the modem for this port (serial KISS, NinoTNC,
    /// KISS-over-TCP, or AXUDP). A discriminated union keyed by its <c>kind</c>.</summary>
    public required TransportConfig Transport { get; init; }

    /// <summary>
    /// Optional named channel-tuning profile (e.g. <c>slow-afsk1200</c>). Opt-in:
    /// it fills only the AX.25 / KISS fields the operator left unset on this port —
    /// an explicit value always wins — and absence means exact spec defaults. See
    /// <see cref="ChannelProfiles"/> for what each profile sets and why it is a
    /// named per-port choice rather than a silent node-wide default.
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>AX.25 listener parameters for this port (timers, window, retries).
    /// Null = spec defaults (or the <see cref="Profile"/>'s value, if a profile is
    /// set and the field is unset here).</summary>
    public Ax25PortParams? Ax25 { get; init; }

    /// <summary>KISS modem tuning (TXDELAY, persistence, slot time) applied live
    /// once the port is up. Null = leave the modem at its power-on defaults.</summary>
    public KissParams? Kiss { get; init; }

    /// <summary>
    /// Optional per-port ID-beacon override. Null = inherit the system default
    /// (<see cref="NodeConfig.Beacon"/>) wholesale. When present, its
    /// <see cref="PortBeaconConfig.Enabled"/> flag wins outright, and its nullable
    /// <see cref="PortBeaconConfig.IntervalMinutes"/> / <see cref="PortBeaconConfig.Text"/>
    /// fields fill in from the system default when left null (a per-field merge —
    /// see <see cref="EffectiveBeacon"/>).
    /// </summary>
    public PortBeaconConfig? Beacon { get; init; }
}

/// <summary>
/// Per-port AX.25 listener tuning. Each value is optional; an unset value means
/// "use the engine's spec default". These map onto <c>Ax25ListenerOptions</c>.
/// </summary>
/// <remarks>
/// Changing any of these is a <b>new-sessions-only</b> change — the reconcile
/// rebuilds the listener's option seed so future sessions pick up the new
/// values, but it never reaches into a live session's negotiated context.
/// </remarks>
public sealed record Ax25PortParams
{
    /// <summary>T1 acknowledgement timer seed, milliseconds. Null = engine default.</summary>
    public int? T1Ms { get; init; }

    /// <summary>T2 response-delay timer, milliseconds. Null = engine default.</summary>
    public int? T2Ms { get; init; }

    /// <summary>T3 inactive-link timer, milliseconds. Null = engine default.</summary>
    public int? T3Ms { get; init; }

    /// <summary>N2 maximum retries before giving up. Null = engine default (10).</summary>
    public int? N2 { get; init; }

    /// <summary>Send-window size k. Null = engine default (4 for mod-8).</summary>
    public int? WindowSize { get; init; }

    /// <summary>LRU cap on cached per-peer sessions. Null = engine default (64).</summary>
    public int? MaxCachedPeers { get; init; }
}

/// <summary>
/// KISS modem tuning knobs, all in the units the KISS spec uses. Each is
/// optional. Applied via the <c>IKissModem</c> setters once the port is up, and
/// re-applied live on a hot reconfigure (no port restart).
/// </summary>
public sealed record KissParams
{
    /// <summary>KISS TXDELAY (0x01), in units of 10 ms.</summary>
    public byte? TxDelay { get; init; }

    /// <summary>KISS PERSIST (0x02), 0..255.</summary>
    public byte? Persistence { get; init; }

    /// <summary>KISS SLOTTIME (0x03), in units of 10 ms.</summary>
    public byte? SlotTime { get; init; }

    /// <summary>KISS TXTAIL (0x04), in units of 10 ms. Most modern modems ignore it.</summary>
    public byte? TxTail { get; init; }
}

/// <summary>Operator-facing service strings, hot-swappable by reference.</summary>
public sealed record ServicesConfig
{
    /// <summary>Welcome banner shown on every new console connection (telnet or
    /// over-the-air). <c>{node}</c> and <c>{call}</c> placeholders are expanded.</summary>
    public string Banner { get; init; } = "Welcome to {node} ({call})";

    /// <summary>The command prompt emitted after the banner and after each
    /// command. <c>{call}</c> is expanded.</summary>
    public string Prompt { get; init; } = "{call}> ";
}

/// <summary>
/// The node's system-default ID beacon: a periodic connectionless AX.25 UI frame
/// (an "ID"/presence broadcast) transmitted on each port. <b>Default-OFF</b> —
/// <see cref="Enabled"/> defaults <c>false</c> so a stock node never transmits an
/// unsolicited beacon until the operator opts in (the no-regression contract). A
/// port may override this with <see cref="PortConfig.Beacon"/>.
/// </summary>
public sealed record BeaconConfig
{
    /// <summary>Whether the node beacons on its ports by default. Default
    /// <c>false</c> — a node that has never beaconed must keep not beaconing.</summary>
    public bool Enabled { get; init; }

    /// <summary>Minutes between beacon transmissions on a port. Default 30.</summary>
    public int IntervalMinutes { get; init; } = 30;

    /// <summary>The beacon's information text. <c>{node}</c> (alias else callsign)
    /// and <c>{call}</c> (the station callsign) placeholders are expanded — exactly
    /// like the services banner / prompt. Default <c>"{node} pdn node"</c>.</summary>
    public string Text { get; init; } = "{node} pdn node";
}

/// <summary>
/// A per-port ID-beacon override. <see cref="Enabled"/> always wins outright; the
/// nullable <see cref="IntervalMinutes"/> / <see cref="Text"/> fields inherit the
/// system default (<see cref="BeaconConfig"/>) when left null — a per-field merge.
/// </summary>
public sealed record PortBeaconConfig
{
    /// <summary>Whether this port beacons. This flag is authoritative for the port —
    /// it is not merged: a port-override with <c>Enabled = false</c> silences a port
    /// even when the system default is on, and vice-versa.</summary>
    public bool Enabled { get; init; }

    /// <summary>Minutes between this port's beacons. Null = inherit the system
    /// default's <see cref="BeaconConfig.IntervalMinutes"/>.</summary>
    public int? IntervalMinutes { get; init; }

    /// <summary>This port's beacon text (<c>{node}</c>/<c>{call}</c> expanded). Null =
    /// inherit the system default's <see cref="BeaconConfig.Text"/>.</summary>
    public string? Text { get; init; }
}

/// <summary>
/// The fully-resolved beacon for one port — the per-port override (if any) merged
/// over the system default. This is what the <c>BeaconService</c> arms a timer from.
/// </summary>
/// <param name="Enabled">Whether to beacon on this port at all.</param>
/// <param name="IntervalMinutes">Resolved transmit interval, minutes (≥ 1).</param>
/// <param name="Text">Resolved beacon text, with <c>{node}</c>/<c>{call}</c> still unexpanded.</param>
public readonly record struct EffectiveBeacon(bool Enabled, int IntervalMinutes, string Text)
{
    /// <summary>
    /// Resolve the effective beacon for a port: the per-port <paramref name="port"/>
    /// override merged over the system <paramref name="systemDefault"/>. When the port
    /// has no override the system default applies wholesale; when it has one, its
    /// <see cref="PortBeaconConfig.Enabled"/> wins outright and its null interval/text
    /// fall back to the system default's.
    /// </summary>
    public static EffectiveBeacon Resolve(BeaconConfig systemDefault, PortBeaconConfig? port)
    {
        ArgumentNullException.ThrowIfNull(systemDefault);
        if (port is null)
        {
            return new EffectiveBeacon(systemDefault.Enabled, systemDefault.IntervalMinutes, systemDefault.Text);
        }
        return new EffectiveBeacon(
            port.Enabled,
            port.IntervalMinutes ?? systemDefault.IntervalMinutes,
            port.Text ?? systemDefault.Text);
    }
}

/// <summary>Management-surface configuration: the local telnet console and the
/// (slice-1 present-but-inert) web server.</summary>
public sealed record ManagementConfig
{
    /// <summary>The local dial-in telnet console.</summary>
    public TelnetConfig Telnet { get; init; } = new();

    /// <summary>The web server bind. Slice 1 maps only <c>GET /healthz</c>;
    /// API/auth/UI are later slices.</summary>
    public HttpConfig Http { get; init; } = new();

    /// <summary>Web control-API authentication. Default-OFF (see
    /// <see cref="AuthConfig"/>): with it off the API behaves exactly as it did
    /// before auth existed — the read / SSE / config / ports / sessions / ping
    /// endpoints and the SPA all serve unauthenticated. With it on, a JWT bearer
    /// token is required and the per-endpoint scope gates enforce.</summary>
    public AuthConfig Auth { get; init; } = new();
}

/// <summary>
/// Web control-API authentication configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Default-OFF, no regression.</b> <see cref="Enabled"/> defaults to
/// <c>false</c>: the auth machinery (user store, JWT issuing/validation, the
/// scope policies) is always wired, but <em>enforcement</em> is conditional on
/// this flag. With it off, every endpoint that would otherwise be gated serves
/// unauthenticated exactly as before — so turning auth on is a deliberate,
/// reviewed step and never a silent behaviour change for an existing node.
/// </para>
/// <para>
/// The signing key and the user records live in <c>pdn.db</c> (the consolidated
/// SQLite store), not here — this config record only carries the on/off switch
/// and the token lifetime. The key is generated on first start and persisted;
/// it is never written to config or logs.
/// </para>
/// </remarks>
public sealed record AuthConfig
{
    /// <summary>Whether the web control API requires authentication. Default
    /// <c>false</c> — the API is unauthenticated until the operator opts in. When
    /// <c>true</c>, a JWT bearer token is required on the gated endpoints and the
    /// <c>read</c>/<c>operate</c>/<c>admin</c> scope policies enforce.</summary>
    public bool Enabled { get; init; }

    /// <summary>Access-token lifetime in minutes. Null = the default (60 — ~1h).
    /// No refresh tokens in v1, so this is the full session length before a
    /// re-login.</summary>
    public int? AccessTokenMinutes { get; init; }
}

/// <summary>
/// The local telnet console listener. Defaults to loopback-only — the console
/// is operator-local dial-in, not a network service.
/// </summary>
public sealed record TelnetConfig
{
    /// <summary>Whether to run the telnet console at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Bind address. Defaults to <c>127.0.0.1</c> — loopback only.</summary>
    public string Bind { get; init; } = "127.0.0.1";

    /// <summary>TCP port for the telnet console.</summary>
    public int Port { get; init; } = 8011;
}

/// <summary>The web server bind. Present-but-inert in slice 1.</summary>
public sealed record HttpConfig
{
    /// <summary>Bind address for Kestrel.</summary>
    public string Bind { get; init; } = "127.0.0.1";

    /// <summary>TCP port for the web server.</summary>
    public int Port { get; init; } = 8080;
}

/// <summary>
/// NET/ROM configuration. The node always <b>hears</b> NODES routing broadcasts
/// (UI frames to dest <c>NODES</c>, PID 0xCF) via the frame-trace tap, parses
/// them, and builds a routing table surfaced in <c>Nodes</c> / a future MCP tool —
/// the read-only awareness slice. With <see cref="Broadcast"/> on it also
/// <b>originates</b> its own NODES broadcast on the NODESINTERVAL schedule, and
/// with <see cref="Connect"/> on it can establish <b>L4 virtual circuits</b> over
/// connected-mode AX.25 interlinks so <c>connect &lt;alias&gt;</c> routes a user to
/// a distant node across the network, and (<see cref="Forward"/>, on by default
/// under <see cref="Connect"/>) it <b>forwards transit datagrams</b> for other
/// stations — the full network-layer routing role.
/// </summary>
/// <remarks>
/// The knobs are exposed because NET/ROM has no single normative standard — the
/// canonical defaults apply unless the operator overrides, never a silent BPQ-ism.
/// Default <see cref="Enabled"/> is <c>true</c> (hearing is free + harmless), but
/// the TX-bearing gates (<see cref="Broadcast"/>, <see cref="Connect"/>) default
/// <c>false</c>: a stock node does not transmit on the air or open interlinks until
/// the operator opts in (spec-faithful + safe-by-default). <see cref="Forward"/>
/// defaults <c>true</c> but rides on <see cref="Connect"/>, so it too is silent
/// until the operator opts into interlinks — at which point the node behaves as a
/// real NET/ROM node and relays transit traffic.
/// </remarks>
public sealed record NetRomConfig
{
    /// <summary>Whether to listen for NODES broadcasts and maintain the routing
    /// table. Default <c>true</c> (read-only, harmless). Set <c>false</c> to make
    /// the node deaf to NET/ROM entirely (also disables broadcast + connect).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Whether to <b>originate</b> our own NODES routing broadcast (and so advertise
    /// our presence + learned routes to neighbours). Default <c>false</c> —
    /// transmitting on the air is opt-in. Requires <see cref="Enabled"/>.
    /// </summary>
    public bool Broadcast { get; init; }

    /// <summary>
    /// Whether <c>connect &lt;alias&gt;</c> may route across the network via NET/ROM
    /// L4 circuits (open an interlink to the best neighbour + originate a circuit to
    /// the distant node). Default <c>false</c> — opening interlinks is opt-in.
    /// Requires <see cref="Enabled"/>.
    /// </summary>
    public bool Connect { get; init; }

    /// <summary>
    /// Whether this node <b>forwards transit datagrams</b> — relays a NET/ROM L3
    /// datagram whose destination node is not us onward toward its destination's best
    /// neighbour (TTL-decremented, hop-by-hop). This is the network-layer routing
    /// role: without it the node is an endpoint only (it originates + terminates
    /// circuits but never carries third-party traffic). Default <c>true</c>, but it
    /// is <b>only effective when <see cref="Connect"/> is on</b> — forwarding needs
    /// the connected-mode interlink machinery that <see cref="Connect"/> gates, and a
    /// node that has not opted into on-air interlinks cannot relay. So a stock node
    /// (<see cref="Connect"/> off) stays silent; a connect-enabled node is a full
    /// NET/ROM node and forwards by default; set this <c>false</c> to run an
    /// originate-only node that does not carry transit traffic.
    /// </summary>
    public bool Forward { get; init; } = true;

    /// <summary>
    /// How a forwarding node picks among multiple kept routes to a destination
    /// (<see cref="NetRomForwardMode"/>). Default
    /// <see cref="NetRomForwardMode.PerFlow"/> — a transit node spreads distinct L4
    /// circuits across the kept routes, quality-weighted, each circuit pinned to one
    /// path (so its ordering is preserved). Set <see cref="NetRomForwardMode.BestRoute"/>
    /// to always use the single best route. Only consulted when <see cref="Forward"/>
    /// is on.
    /// </summary>
    public NetRomForwardMode ForwardMode { get; init; } = NetRomForwardMode.PerFlow;

    /// <summary>
    /// Our NET/ROM node alias / mnemonic, advertised in our NODES broadcast (the
    /// 6-char field). Null/empty = fall back to the node identity alias (then the
    /// callsign). Only the first 6 characters reach the wire.
    /// </summary>
    public string? Alias { get; init; }

    /// <summary>Path quality assumed for a directly-heard neighbour (the canonical
    /// default-port quality). Null = canonical default (192).</summary>
    public int? DefaultNeighbourQuality { get; init; }

    /// <summary>Worst quality a learned route may have and still be kept (MINQUAL).
    /// Null = canonical default (0 — keep everything above zero).</summary>
    public int? MinQuality { get; init; }

    /// <summary>Obsolescence count a route is (re)initialised to on a broadcast
    /// (OBSINIT). Null = canonical default (6).</summary>
    public int? ObsoleteInitial { get; init; }

    /// <summary>The obsolescence advertise-gate (OBSMIN): a route below this is kept
    /// but no longer included in our outgoing broadcasts. Null = canonical default (4).</summary>
    public int? ObsoleteMinimum { get; init; }

    /// <summary>Seconds between obsolescence sweeps + (when <see cref="Broadcast"/>)
    /// NODES broadcasts — the canonical NODESINTERVAL. Null = default (3600 — once an
    /// hour).</summary>
    public int? SweepIntervalSeconds { get; init; }

    /// <summary>The L4 circuit send-window we propose / accept (BPQ <c>L4WINDOW</c>).
    /// Null = canonical default (4).</summary>
    public int? Window { get; init; }

    /// <summary>The L4 retransmit timeout in seconds (BPQ <c>L4TIMEOUT</c>-ish). Null
    /// = default (5 s).</summary>
    public int? TransportTimeoutSeconds { get; init; }

    /// <summary>Max L4 retransmit attempts before a circuit fails (BPQ
    /// <c>L4RETRIES</c>). Null = default (3).</summary>
    public int? TransportRetries { get; init; }

    /// <summary>Initial L3 network-header time-to-live (hop limit) on circuits we
    /// originate (BPQ <c>L3TIMETOLIVE</c>). Null = default (25).</summary>
    public int? TimeToLive { get; init; }

    /// <summary>
    /// The INP3 link-timing routing overlay (default-off). When
    /// <see cref="NetRomInp3Options.Enabled"/> is <c>false</c> — which is the
    /// default, since the property initialises to <c>new()</c> ⇒
    /// <see cref="NetRomInp3Options.Default"/> — the node behaves byte-for-byte as
    /// today: no L3RTT probing, no RIF ingest/emit, no INP3 routes. INP3 is an
    /// opt-in overlay on the vanilla quality-based NET/ROM stack; it requires both
    /// <see cref="Enabled"/> and <see cref="Connect"/> (the L3RTT / RIF frames ride
    /// the connected-mode interlink machinery <see cref="Connect"/> gates, so the host
    /// constructs the overlay only under Connect — the validator rejects
    /// <c>inp3.enabled</c> without <c>connect</c> rather than silently no-op).
    /// </summary>
    /// <remarks>
    /// Unlike the nullable-overlay knobs above (<see cref="Window"/> etc., which are
    /// resolved field-by-field against a lib <c>Default</c>), this binds the whole
    /// <see cref="NetRomInp3Options"/> record directly: it is one validated record
    /// (its own <see cref="NetRomInp3Options.Validate"/> is the single source of
    /// truth for the knob ranges), it is pure durations / ints / bools (no
    /// discriminated union, no <c>Callsign</c> struct), and an absent nested
    /// <c>inp3:</c> key simply leaves the C# default in place under the existing
    /// <c>IgnoreUnmatchedProperties</c> + camel-case deserializer. The
    /// <see cref="System.TimeSpan"/>-typed knobs (<see cref="NetRomInp3Options.L3RttInterval"/>,
    /// <see cref="NetRomInp3Options.L3RttResetWindow"/>, <see cref="NetRomInp3Options.RifInterval"/>,
    /// <see cref="NetRomInp3Options.PositiveDebounce"/>) carry as YAML duration scalars
    /// (e.g. <c>l3RttInterval: 00:01:00</c>) via YamlDotNet's built-in
    /// <c>TimeSpan</c> converter. See docs/netrom-inp3-host-integration-design.md §2.
    /// </remarks>
    public NetRomInp3Options Inp3 { get; init; } = new();
}
