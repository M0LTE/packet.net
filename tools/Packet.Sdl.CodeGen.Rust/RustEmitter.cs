using System.Globalization;
using System.Text;
using Packet.Sdl.IR;

namespace Packet.Sdl.CodeGen.Rust;

/// <summary>
/// Rust emitter for the resolved SDL IR. Produces one <c>.g.rs</c> file
/// per page in the <c>ax25sdl</c> crate (under <c>rust-spec/src/</c>).
/// Hand-rolled string emission (no template engine) — mirrors the Go
/// emitter's pattern.
/// </summary>
/// <remarks>
/// <para>
/// Unlike Go (separate <c>.g_test.go</c>) and TypeScript (separate
/// <c>.g.test.ts</c>), Rust idiomatically nests per-module tests under
/// <c>#[cfg(test)] mod tests { ... }</c> at the bottom of the same file.
/// We emit the data table and its corresponding tests into one combined
/// <c>.g.rs</c> file per state page; <see cref="EmitStatePage"/> is the
/// only emission entrypoint for state-machine pages.
/// </para>
/// <para>
/// The output is post-processed by <c>rustfmt</c> in the orchestrator —
/// we aim for output that's already close to canonical, and let
/// <c>rustfmt</c> handle the last-mile whitespace alignment.
/// </para>
/// </remarks>
public static class RustEmitter
{
    public sealed record Emission(string FileName, string Content);

    public static Emission EmitStatePage(ResolvedPage page)
    {
        var stem = Path.GetFileNameWithoutExtension(page.SourcePath)
            .Replace(".sdl", string.Empty, StringComparison.Ordinal);
        var fileName = stem + ".g.rs";

        // Rust convention: SCREAMING_SNAKE_CASE for `pub static` items.
        // Machine + state: data_link + Disconnected → DATA_LINK_DISCONNECTED.
        var staticName = (ScreamingSnake(page.Machine) + "_" + ScreamingSnake(page.State)).ToUpperInvariant();

        var sb = new StringBuilder();
        EmitHeader(sb, page.SourcePath);
        sb.Append("use crate::types::*;\n\n");
        sb.Append("/// SDL transitions for the ").Append(page.State)
          .Append(" state of the ").Append(page.Machine).Append(" machine.\n");
        sb.Append("/// Source: ").Append(page.SourceSpec).Append(", figure ").Append(page.SourceFigure).Append(".\n");
        sb.Append("pub static ").Append(staticName).Append(": StatePage = StatePage {\n");
        sb.Append("    machine: ").Append(RustStringLiteral(page.Machine)).Append(",\n");
        sb.Append("    state: ").Append(RustStringLiteral(page.State)).Append(",\n");
        sb.Append("    source: SdlSource {\n");
        sb.Append("        spec: ").Append(RustStringLiteral(page.SourceSpec)).Append(",\n");
        sb.Append("        figure: ").Append(RustStringLiteral(page.SourceFigure)).Append(",\n");
        sb.Append("        url: ").Append(RustStringLiteral(page.SourceUrl ?? "")).Append(",\n");
        sb.Append("    },\n");
        sb.Append("    transitions: &[\n");
        foreach (var t in page.Transitions)
        {
            sb.Append("        TransitionSpec {\n");
            sb.Append("            id: ").Append(RustStringLiteral(t.Id)).Append(",\n");
            sb.Append("            from: ").Append(RustStringLiteral(page.State)).Append(",\n");
            sb.Append("            on: ").Append(RustStringLiteral(t.On)).Append(",\n");
            sb.Append("            guard: ").Append(RustStringLiteral(t.Guard ?? "")).Append(",\n");
            sb.Append("            actions: ").Append(FormatActions(t.Actions, 3)).Append(",\n");
            sb.Append("            next: ").Append(RustStringLiteral(t.Next)).Append(",\n");
            sb.Append("            notes: ").Append(RustStringLiteral(t.Notes ?? "")).Append(",\n");
            sb.Append("            references: ").Append(FormatReferences(t.References, 3)).Append(",\n");
            sb.Append("            loops: ").Append(FormatLoops(t.Loops, 3)).Append(",\n");
            sb.Append("        },\n");
        }
        sb.Append("    ],\n");
        sb.Append("};\n");

        // Per-transition tests (inline mod, idiomatic Rust). Mirrors the
        // C# .g.Tests.cs / Go .g_test.go scope: one test per transition
        // checking id/on/next/guard plus every action's verb + kind.
        sb.Append('\n');
        sb.Append("#[cfg(test)]\n");
        sb.Append("mod tests {\n");
        sb.Append("    use super::*;\n\n");

        sb.Append("    #[test]\n");
        sb.Append("    fn source_figure() {\n");
        sb.Append("        assert_eq!(").Append(staticName).Append(".source.figure, ")
          .Append(RustStringLiteral(page.SourceFigure)).Append(");\n");
        sb.Append("    }\n\n");

        sb.Append("    #[test]\n");
        sb.Append("    fn transitions_are_present() {\n");
        sb.Append("        assert_eq!(").Append(staticName).Append(".transitions.len(), ")
          .Append(page.Transitions.Count.ToString(CultureInfo.InvariantCulture)).Append(");\n");
        sb.Append("    }\n");

        foreach (var t in page.Transitions)
        {
            sb.Append('\n');
            sb.Append("    #[test]\n");
            sb.Append("    fn ").Append(t.Id).Append("() {\n");
            sb.Append("        let tx = ").Append(staticName).Append(".transitions.iter()\n");
            sb.Append("            .find(|x| x.id == ").Append(RustStringLiteral(t.Id)).Append(")\n");
            sb.Append("            .expect(\"transition ").Append(t.Id).Append(" not found\");\n");
            sb.Append("        assert_eq!(tx.on, ").Append(RustStringLiteral(t.On)).Append(");\n");
            sb.Append("        assert_eq!(tx.next, ").Append(RustStringLiteral(t.Next)).Append(");\n");
            if (!string.IsNullOrEmpty(t.Guard))
            {
                sb.Append("        assert_eq!(tx.guard, ").Append(RustStringLiteral(t.Guard!)).Append(");\n");
            }
            sb.Append("        assert_eq!(tx.actions.len(), ")
              .Append(t.Actions.Count.ToString(CultureInfo.InvariantCulture)).Append(");\n");
            for (int i = 0; i < t.Actions.Count; i++)
            {
                var a = t.Actions[i];
                var idx = i.ToString(CultureInfo.InvariantCulture);
                sb.Append("        assert_eq!(tx.actions[").Append(idx).Append("].verb, ")
                  .Append(RustStringLiteral(a.Verb)).Append(");\n");
                sb.Append("        assert_eq!(tx.actions[").Append(idx).Append("].kind, ")
                  .Append(RustKindLiteral(a.Kind)).Append(");\n");
            }
            sb.Append("    }\n");
        }
        sb.Append("}\n");

        return new Emission(fileName, sb.ToString());
    }

    public static Emission EmitSubroutinePage(ResolvedSubroutinesPage page)
    {
        var fileStem = Path.GetFileNameWithoutExtension(page.SourcePath)
            .Replace(".sdl", string.Empty, StringComparison.Ordinal);
        var staticName = (ScreamingSnake(page.Machine) + "_" + ScreamingSnake(fileStem)).ToUpperInvariant();
        var fileName = fileStem + ".g.rs";

        var sb = new StringBuilder();
        EmitHeader(sb, page.SourcePath);
        sb.Append("use crate::types::*;\n\n");
        sb.Append("/// SDL subroutines for the ").Append(page.Machine).Append(" machine.\n");
        sb.Append("/// Source: ").Append(page.SourceSpec).Append(", figure ").Append(page.SourceFigure).Append(".\n");
        sb.Append("pub static ").Append(staticName).Append(": SubroutinesPage = SubroutinesPage {\n");
        sb.Append("    machine: ").Append(RustStringLiteral(page.Machine)).Append(",\n");
        sb.Append("    source: SdlSource {\n");
        sb.Append("        spec: ").Append(RustStringLiteral(page.SourceSpec)).Append(",\n");
        sb.Append("        figure: ").Append(RustStringLiteral(page.SourceFigure)).Append(",\n");
        sb.Append("        url: ").Append(RustStringLiteral(page.SourceUrl ?? "")).Append(",\n");
        sb.Append("    },\n");
        sb.Append("    subroutines: &[\n");
        foreach (var s in page.Subroutines)
        {
            sb.Append("        SubroutineSpec {\n");
            sb.Append("            name: ").Append(RustStringLiteral(s.Name)).Append(",\n");
            sb.Append("            paths: &[\n");
            foreach (var p in s.Paths)
            {
                sb.Append("                SubroutinePath {\n");
                sb.Append("                    id: ").Append(RustStringLiteral(p.Id)).Append(",\n");
                sb.Append("                    guard: ").Append(RustStringLiteral(p.Guard ?? "")).Append(",\n");
                sb.Append("                    actions: ").Append(FormatActions(p.Actions, 5)).Append(",\n");
                sb.Append("                    notes: ").Append(RustStringLiteral(p.Notes ?? "")).Append(",\n");
                sb.Append("                    references: ").Append(FormatReferences(p.References, 5)).Append(",\n");
                sb.Append("                    loops: ").Append(FormatLoops(p.Loops, 5)).Append(",\n");
                sb.Append("                },\n");
            }
            sb.Append("            ],\n");
            sb.Append("            notes: ").Append(RustStringLiteral(s.Notes ?? "")).Append(",\n");
            sb.Append("            references: ").Append(FormatReferences(s.References, 3)).Append(",\n");
            sb.Append("        },\n");
        }
        sb.Append("    ],\n");
        sb.Append("};\n");

        return new Emission(fileName, sb.ToString());
    }

    /// <summary>
    /// Build the crate's <c>lib.rs</c> that declares each generated
    /// module + the hand-written <c>types</c> module, and re-exports
    /// every page's statics for ergonomic single-import access
    /// (<c>use ax25sdl::DATA_LINK_DISCONNECTED;</c>).
    /// </summary>
    public static string EmitLib(IEnumerable<ResolvedPage> pages, IEnumerable<ResolvedSubroutinesPage> subPages)
    {
        var sb = new StringBuilder();
        sb.Append("// Code generated by tools/Packet.Sdl.CodeGen. DO NOT EDIT.\n");
        sb.Append("// Re-exports every SDL page so consumers can `use ax25sdl::DATA_LINK_DISCONNECTED`.\n\n");
        sb.Append("pub mod types;\n");
        sb.Append("pub use types::*;\n\n");

        var stems = new List<string>();
        foreach (var page in pages.OrderBy(p => p.SourcePath, StringComparer.Ordinal))
        {
            stems.Add(Path.GetFileNameWithoutExtension(page.SourcePath)
                .Replace(".sdl", string.Empty, StringComparison.Ordinal));
        }
        foreach (var page in subPages.OrderBy(p => p.SourcePath, StringComparer.Ordinal))
        {
            stems.Add(Path.GetFileNameWithoutExtension(page.SourcePath)
                .Replace(".sdl", string.Empty, StringComparison.Ordinal));
        }

        foreach (var stem in stems)
        {
            sb.Append("#[path = \"").Append(stem).Append(".g.rs\"]\n");
            sb.Append("pub mod ").Append(stem).Append(";\n");
        }
        sb.Append('\n');
        foreach (var stem in stems)
        {
            sb.Append("pub use ").Append(stem).Append("::*;\n");
        }
        return sb.ToString();
    }

    // ─── Formatting helpers ───────────────────────────────────────────

    private static void EmitHeader(StringBuilder sb, string sourcePath)
    {
        sb.Append("// Code generated by tools/Packet.Sdl.CodeGen from ").Append(sourcePath.Replace('\\', '/')).Append(".\n");
        sb.Append("// DO NOT EDIT. Run `dotnet run --project tools/Packet.Sdl.CodeGen` to regenerate.\n\n");
    }

    /// <summary>
    /// <paramref name="parentIndent"/> is the indentation depth (in
    /// 4-space units) of the surrounding <c>field: &amp;[</c> line.
    /// Entries land at parentIndent+1; the closing bracket matches
    /// parentIndent. Matches rustfmt's expected nesting for static
    /// slice initialisers.
    /// </summary>
    private static string FormatActions(IReadOnlyList<ResolvedAction> actions, int parentIndent)
    {
        if (actions.Count == 0) return "&[]";
        var indent = new string(' ', (parentIndent + 1) * 4);
        var closer = new string(' ', parentIndent * 4);
        var sb = new StringBuilder();
        sb.Append("&[\n");
        foreach (var a in actions)
        {
            sb.Append(indent).Append("ActionStep { verb: ").Append(RustStringLiteral(a.Verb))
              .Append(", kind: ").Append(RustKindLiteral(a.Kind)).Append(" },\n");
        }
        sb.Append(closer).Append(']');
        return sb.ToString();
    }

    private static string FormatReferences(IReadOnlyList<ResolvedReference> refs, int parentIndent)
    {
        if (refs.Count == 0) return "&[]";
        var indent = new string(' ', (parentIndent + 1) * 4);
        var closer = new string(' ', parentIndent * 4);
        var sb = new StringBuilder();
        sb.Append("&[\n");
        foreach (var r in refs)
        {
            sb.Append(indent).Append("ImplementationReference { source: ").Append(RustStringLiteral(r.Source))
              .Append(", cite: ").Append(RustStringLiteral(r.Cite ?? ""))
              .Append(", quote: ").Append(RustStringLiteral(r.Quote ?? ""))
              .Append(", path: ").Append(RustStringLiteral(r.Path ?? ""))
              .Append(", function: ").Append(RustStringLiteral(r.Function ?? ""))
              .Append(", line: ").Append((r.Line ?? 0).ToString(CultureInfo.InvariantCulture))
              .Append(", note: ").Append(RustStringLiteral(r.Note ?? ""))
              .Append(" },\n");
        }
        sb.Append(closer).Append(']');
        return sb.ToString();
    }

    private static string FormatLoops(IReadOnlyList<ResolvedLoop> loops, int parentIndent)
    {
        if (loops.Count == 0) return "&[]";
        var indent = new string(' ', (parentIndent + 1) * 4);
        var closer = new string(' ', parentIndent * 4);
        var sb = new StringBuilder();
        sb.Append("&[\n");
        foreach (var l in loops)
        {
            sb.Append(indent).Append("LoopRange { start: ").Append(l.Start.ToString(CultureInfo.InvariantCulture))
              .Append(", length: ").Append(l.Length.ToString(CultureInfo.InvariantCulture))
              .Append(", predicate: ").Append(RustStringLiteral(l.Predicate))
              .Append(" },\n");
        }
        sb.Append(closer).Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Rust string literal — double-quoted with backslash escapes.
    /// Avoids raw strings because notes / predicates can in principle
    /// contain <c>#"</c> sequences; standard-escaped literals are the
    /// safe default.
    /// </summary>
    internal static string RustStringLiteral(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20 || c == 0x7f)
                        sb.AppendFormat(CultureInfo.InvariantCulture, "\\x{0:x2}", (int)c);
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Emit the kind as a path expression. Resolves against the
    /// hand-written <c>ActionKind</c> enum in <c>rust-spec/src/types.rs</c>.
    /// </summary>
    internal static string RustKindLiteral(ResolvedActionKind kind) => kind switch
    {
        ResolvedActionKind.SignalUpper => "ActionKind::SignalUpper",
        ResolvedActionKind.SignalLower => "ActionKind::SignalLower",
        ResolvedActionKind.Processing  => "ActionKind::Processing",
        ResolvedActionKind.Subroutine  => "ActionKind::Subroutine",
        ResolvedActionKind.InternalOut => "ActionKind::InternalOut",
        _ => throw new InvalidOperationException($"unknown action kind '{kind}'"),
    };

    /// <summary>
    /// snake_case → SCREAMING_SNAKE_CASE. Also handles input that's
    /// already PascalCase (e.g. <c>AwaitingConnection22</c> → splits
    /// runs of capitals into <c>AWAITING_CONNECTION_22</c>).
    /// </summary>
    internal static string ScreamingSnake(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length + 4);
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '_')
            {
                sb.Append('_');
                continue;
            }
            // Insert an underscore between a lowercase→uppercase
            // boundary and between a letter→digit / digit→letter
            // boundary, but only if the previous character isn't
            // already an underscore.
            if (i > 0)
            {
                var prev = input[i - 1];
                bool boundary =
                    (char.IsLower(prev) && char.IsUpper(c)) ||
                    (char.IsLetter(prev) && char.IsDigit(c)) ||
                    (char.IsDigit(prev) && char.IsLetter(c));
                if (boundary && sb.Length > 0 && sb[sb.Length - 1] != '_')
                {
                    sb.Append('_');
                }
            }
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
