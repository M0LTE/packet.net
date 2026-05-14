namespace Packet.Ax25.Session;

/// <summary>
/// Looks up and invokes SDL subroutine action chains by canonical name.
/// The dispatcher routes every <c>kind: subroutine</c> verb (e.g.
/// <c>Establish_Data_Link</c>, <c>UI_Check</c>, <c>Select_T1_Value</c>)
/// through this interface; production wires this against figc4.7's
/// subroutine transcriptions, tests can register custom recorders.
/// </summary>
public interface ISubroutineRegistry
{
    /// <summary>
    /// Invoke the subroutine identified by <paramref name="name"/> in
    /// the supplied transition context. Implementations decide how to
    /// handle unknown names — the default registry throws, so a
    /// transcription typo doesn't silently no-op.
    /// </summary>
    void Invoke(string name, TransitionContext tx);
}

/// <summary>
/// Pre-populated subroutine registry. Knows every subroutine name the
/// transcribed pages reference, registers each as a no-op stub by
/// default, and throws on unknown names so transcription typos surface
/// immediately.
/// </summary>
/// <remarks>
/// <para>
/// The no-op stubs are placeholders. figc4.7 (Subroutines) is the
/// figure that gives each subroutine its action body — that
/// transcription work is the next step in the SDL arc. Until then, the
/// dispatcher can run a transition's full action chain through
/// orchestrator + real ActionDispatcher; the subroutine bodies just
/// don't *do* anything specific. The orchestrator routing, the
/// context-variable mutations, the timer ops, the frame emissions
/// surrounding the subroutine calls all work end-to-end today.
/// </para>
/// <para>
/// To override a stub for testing, use <see cref="Register"/>:
/// <code>
/// var registry = new DefaultSubroutineRegistry();
/// registry.Register("Establish_Data_Link", tx => {
///     // … test-specific behaviour, e.g. record the call …
/// });
/// </code>
/// </para>
/// </remarks>
public sealed class DefaultSubroutineRegistry : ISubroutineRegistry
{
    private readonly Dictionary<string, Action<TransitionContext>> subroutines = new(StringComparer.Ordinal);

    /// <summary>Canonical names of every subroutine the transcribed pages reference.</summary>
    public static IReadOnlyList<string> KnownSubroutines { get; } = new[]
    {
        // figc4.1 (Disconnected) + figc4.4 (Connected)
        "Establish_Data_Link",
        "Clear_Exception_Conditions",
        // figc4.2 / 4.3 / 4.6
        "UI_Check",
        "Select_T1_Value",
        // figc4.4 (Connected) — I-frame and ack flow
        "Check_I_Frame_Acknowledged",
        "Check_I_Frames_Acknowledged",
        "Check_Need_For_Response",
        "Transmit_Enquiry",
        "Invoke_Retransmission",
        "N_r_Error_Recovery",
        "Enquiry_Response_F_0",
        "Enquiry_Response_F_1",
    };

    /// <summary>
    /// Construct a registry with no-op stubs registered for every known
    /// subroutine. Each stub silently succeeds — figc4.7 transcription
    /// is what will give them real bodies.
    /// </summary>
    public DefaultSubroutineRegistry()
    {
        foreach (var name in KnownSubroutines)
        {
            subroutines[name] = _ => { /* TODO: figc4.7 transcription */ };
        }
    }

    /// <summary>
    /// Register a custom implementation for the named subroutine. Replaces
    /// any existing entry (including the default no-op stub) so tests can
    /// observe / mock subroutine calls.
    /// </summary>
    public void Register(string name, Action<TransitionContext> implementation)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(implementation);
        subroutines[name] = implementation;
    }

    /// <inheritdoc/>
    public void Invoke(string name, TransitionContext tx)
    {
        if (!subroutines.TryGetValue(name, out var impl))
        {
            throw new InvalidOperationException(
                $"unknown SDL subroutine: '{name}'. " +
                "Known subroutines are registered in DefaultSubroutineRegistry — add a Register() call or update KnownSubroutines.");
        }
        impl(tx);
    }
}
