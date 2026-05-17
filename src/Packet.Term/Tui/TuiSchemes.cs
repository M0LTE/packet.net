using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using TguiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Packet.Term.Tui;

/// <summary>
/// Custom Terminal.Gui colour schemes for the frame monitor and
/// conversation panes. Built once at startup and registered with the
/// global <see cref="SchemeManager"/> by the names declared here.
/// </summary>
/// <remarks>
/// We keep the names short and explicit (<c>"PacketTerm.Monitor"</c>,
/// <c>"PacketTerm.Chat"</c>) so they don't collide with the built-in
/// <c>Base</c> / <c>Dialog</c> / <c>Menu</c> entries. The colour choice
/// is deliberately cool (cyan-on-dark) for the monitor and warmer
/// (white-on-dark with green accent) for the chat. Direction markers in
/// the monitor (T / R) are coloured by injecting ANSI-style runs at the
/// view level — Terminal.Gui's <see cref="Terminal.Gui.Views.TextView"/>
/// doesn't support per-line colouring directly, so we lean on the
/// scheme for the global palette and accept BPQ-monitor-style
/// uniformity within each pane.
/// </remarks>
internal static class TuiSchemes
{
    /// <summary>Scheme name applied to the frame-monitor TextView.</summary>
    public const string Monitor = "PacketTerm.Monitor";

    /// <summary>Scheme name applied to the conversation TextView.</summary>
    public const string Chat = "PacketTerm.Chat";

    /// <summary>Scheme name applied to the input line TextField.</summary>
    public const string Input = "PacketTerm.Input";

    /// <summary>Scheme name applied to the status bar.</summary>
    public const string Status = "PacketTerm.Status";

    /// <summary>
    /// Build and install the four custom schemes. Safe to call twice —
    /// re-registering an existing name overwrites it.
    /// </summary>
    public static void Register()
    {
        SchemeManager.AddScheme(Monitor, BuildMonitor());
        SchemeManager.AddScheme(Chat, BuildChat());
        SchemeManager.AddScheme(Input, BuildInput());
        SchemeManager.AddScheme(Status, BuildStatus());
    }

    private static Scheme BuildMonitor()
    {
        // Cool palette: cyan-on-black. Mirrors the original Spectre intent
        // (Color.Cyan1 panel title + dimmed body).
        var normal = new TguiAttribute(Color.Cyan, Color.Black);
        var focus = new TguiAttribute(Color.BrightCyan, Color.Black);
        return Build(normal, focus);
    }

    private static Scheme BuildChat()
    {
        // Warmer palette: light-on-dark. The "*** ..." sentinel lines used
        // to be yellow under Spectre; we lose per-line tinting under
        // TextView but keep the warm overall feel via the foreground.
        var normal = new TguiAttribute(Color.White, Color.Black);
        var focus = new TguiAttribute(Color.BrightYellow, Color.Black);
        return Build(normal, focus);
    }

    private static Scheme BuildInput()
    {
        // Input prompt — yellow on dark blue mimics Turbo Vision's edit
        // line feel. Disabled state stays muted.
        var normal = new TguiAttribute(Color.BrightYellow, Color.Blue);
        var focus = new TguiAttribute(Color.White, Color.Blue);
        return Build(normal, focus);
    }

    private static Scheme BuildStatus()
    {
        // Classic Turbo Vision status bar — black on cyan.
        var normal = new TguiAttribute(Color.Black, Color.Cyan);
        var focus = new TguiAttribute(Color.Black, Color.BrightCyan);
        return Build(normal, focus);
    }

    private static Scheme Build(TguiAttribute normal, TguiAttribute focus)
    {
        // VisualRole drives how the scheme renders in different states.
        // We initialise all reasonable roles so an unusual draw call
        // doesn't fall through to a parent scheme.
        return new Scheme(normal)
        {
            Normal = normal,
            Focus = focus,
            HotNormal = normal,
            HotFocus = focus,
            Disabled = new TguiAttribute(Color.DarkGray, Color.Black),
        };
    }
}
