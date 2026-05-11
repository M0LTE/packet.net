namespace Packet.Ax25.Sdl;

/// <summary>
/// One SDL transition: when in <see cref="From"/> and we receive event
/// <see cref="On"/> while <see cref="Guard"/> holds, run <see cref="Actions"/>
/// and move to <see cref="Next"/>.
/// </summary>
/// <remarks>
/// Guard and action strings are opaque to the codegen — they describe spec
/// intent. The orchestrator in Packet.Ax25 maps them to concrete C# behaviour
/// at runtime.
/// </remarks>
public sealed record TransitionSpec(
    string Id,
    string From,
    string On,
    string? Guard,
    IReadOnlyList<string> Actions,
    string Next,
    string? Notes);

/// <summary>
/// Provenance for a generated state machine page — which spec figure it came
/// from. Surfaced in generated code so a reader can trace any transition back
/// to its source diagram.
/// </summary>
public sealed record SdlSource(
    string Spec,
    string Figure,
    string? Url);
