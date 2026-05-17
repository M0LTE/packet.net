using Packet.Core;
using Terminal.Gui.App;

namespace Packet.Term.Tui;

/// <summary>
/// Terminal.Gui v2 entry-point shim. <see cref="Program"/> hands MYCALL,
/// the serial port name, the already-opened modem, and an optional
/// auto-connect target here; this method owns the Terminal.Gui
/// <see cref="IApplication"/> lifecycle for the whole TUI session.
/// </summary>
/// <remarks>
/// The v2 idiomatic pattern is <c>app = Application.Create(); app.Init();
/// app.Run(window); app.Dispose()</c>. The static <see cref="Application"/>
/// surface from v1 is marked obsolete in 2.1 and would break our
/// TreatWarningsAsErrors build.
/// </remarks>
public static class PacketTermApp
{
    /// <summary>
    /// Bring up Terminal.Gui, drive the main window until the user quits,
    /// then shut down cleanly. Synchronous — blocks the caller.
    /// </summary>
    public static void Run(Callsign myCall, string portName, KissSerialModem modem, Callsign? autoConnect)
    {
        ArgumentNullException.ThrowIfNull(modem);

        IApplication app = Application.Create();
        app.Init();
        TuiSchemes.Register();
        try
        {
            using var window = new MainWindow(app, myCall, portName, modem);
            window.AttachAutoConnect(autoConnect);
            app.Run(window);
        }
        finally
        {
            if (app is IDisposable d) d.Dispose();
        }
    }
}
