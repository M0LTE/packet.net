using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Packet.Term.Tui;

/// <summary>
/// Modal "Connect to ..." dialog. Pre-fills the input with the last
/// known connect target. <see cref="Result"/> is the raw callsign string
/// the user typed; the caller is responsible for parsing/validating it
/// and emitting a friendly error if it doesn't parse.
/// </summary>
internal sealed class ConnectDialog : Dialog
{
    private readonly TextField input;

    /// <summary>Whether the user cancelled the dialog. (Hides the
    /// inherited <see cref="Dialog.Canceled"/> — we drive it ourselves
    /// from the OK / Cancel button handlers + the Esc key.)</summary>
    public new bool Canceled { get; private set; } = true;

    /// <summary>The string the user typed, or <c>null</c> if cancelled.
    /// (Hides the inherited <see cref="Dialog.Result"/>, which is a
    /// <c>Nullable&lt;int&gt;</c> for button-index style dialogs.)</summary>
    public new string? Result { get; private set; }

    public ConnectDialog(string initial)
    {
        Title = "Connect to peer";
        Width = 50;
        Height = 8;

        var label = new Label
        {
            X = 1,
            Y = 1,
            Text = "Callsign (e.g. M0LTE-1):",
        };
        input = new TextField
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(2),
            Text = initial ?? string.Empty,
        };

        var ok = new Button
        {
            Text = "_OK",
            IsDefault = true,
            X = Pos.Center() - 8,
            Y = Pos.AnchorEnd(2),
        };
        ok.Accepting += (_, e) =>
        {
            e.Handled = true;
            Canceled = false;
            Result = input.Text ?? string.Empty;
            CloseDialog();
        };

        var cancel = new Button
        {
            Text = "_Cancel",
            X = Pos.Center() + 2,
            Y = Pos.AnchorEnd(2),
        };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            Canceled = true;
            Result = null;
            CloseDialog();
        };

        Add(label, input, ok, cancel);

        // Esc dismisses with no result; mirrors Turbo Vision habit.
        KeyDown += (_, e) =>
        {
            if (e == Key.Esc)
            {
                Canceled = true;
                Result = null;
                CloseDialog();
                e.Handled = true;
            }
        };
    }

    private void CloseDialog()
    {
        // The Dialog itself is the IRunnable being stopped; ask the
        // hosting application to end its session.
        App?.RequestStop(this);
    }
}
