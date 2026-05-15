using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Packet.Sdl.CodeGen.Tests;

/// <summary>
/// Black-box tests for the JSON emitter. The emitter validates each
/// emitted page against the generated schema before it writes, so a
/// successful run already guarantees schema-compliance — but the tests
/// re-validate with an independent JsonSchema.Net invocation as a
/// belt-and-braces check that the schema itself is well-formed and
/// rejects garbage.
/// </summary>
public class JsonEmitterTests
{
    private const string MinimalEvents = """
        primitives_upper:
          - DL_DISCONNECT_request
        frames_received:
          - I_received
        catchalls: []
        internal: []
        timers:
          - T1_expiry
        """;

    private const string ValidMinimalPage = """
        machine: data_link
        state: Connected
        coverage: complete
        source:
          spec: test_spec
          figure: figc.test
        decisions: []
        transitions:
          - id: t01_dl_disconnect_request
            on: DL_DISCONNECT_request
            path:
              - { action: send_disc, kind: signal_lower }
            next: AwaitingRelease
        """;

    [Fact]
    public void Json_only_run_produces_g_json_schema_and_index()
    {
        using var r = new CodegenRunner();
        r.WriteEventsCatalog(MinimalEvents);
        r.WritePage("data-link/connected.sdl.yaml", ValidMinimalPage);

        var result = r.RunJson();

        result.ExitCode.Should().Be(0, $"stderr: {result.Stderr}\nstdout: {result.Stdout}");
        r.JsonExists("connected.g.json").Should().BeTrue();
        r.JsonExists("schema.json").Should().BeTrue();
        r.JsonExists("index.json").Should().BeTrue();
    }

    [Fact]
    public void Each_emitted_page_validates_against_schema()
    {
        using var r = new CodegenRunner();
        r.WriteEventsCatalog(MinimalEvents);
        r.WritePage("data-link/connected.sdl.yaml", ValidMinimalPage);
        // A second page so we can be sure the schema admits more than
        // one document in a single run.
        r.WritePage("data-link/disconnected.sdl.yaml", ValidMinimalPage.Replace(
            "state: Connected", "state: Disconnected", StringComparison.Ordinal));

        var result = r.RunJson();
        result.ExitCode.Should().Be(0, $"stderr: {result.Stderr}\nstdout: {result.Stdout}");

        var schema = JsonSchema.FromText(r.ReadJson("schema.json"))!;
        foreach (var file in new[] { "connected.g.json", "disconnected.g.json", "index.json" })
        {
            var doc = JsonNode.Parse(r.ReadJson(file));
            var eval = schema.Evaluate(doc, new EvaluationOptions { OutputFormat = OutputFormat.List });
            eval.IsValid.Should().BeTrue($"{file} failed schema validation: {DescribeFailure(eval)}");
        }
    }

    [Fact]
    public void Schema_is_a_valid_draft_2020_12_document()
    {
        using var r = new CodegenRunner();
        r.WriteEventsCatalog(MinimalEvents);
        r.WritePage("data-link/connected.sdl.yaml", ValidMinimalPage);

        var result = r.RunJson();
        result.ExitCode.Should().Be(0, $"stderr: {result.Stderr}\nstdout: {result.Stdout}");

        var raw = r.ReadJson("schema.json");
        // First: parse as JSON Schema.
        Action parse = () => JsonSchema.FromText(raw);
        parse.Should().NotThrow();

        // Second: pin the dialect ($schema) so a future emitter change
        // can't silently drop us to draft-07.
        var asNode = JsonNode.Parse(raw)!.AsObject();
        asNode["$schema"]!.GetValue<string>().Should().Be("https://json-schema.org/draft/2020-12/schema");
    }

    [Fact]
    public void Json_emission_is_idempotent()
    {
        using var r = new CodegenRunner();
        r.WriteEventsCatalog(MinimalEvents);
        r.WritePage("data-link/connected.sdl.yaml", ValidMinimalPage);

        var first = r.RunJson();
        first.ExitCode.Should().Be(0);
        var firstBytes = File.ReadAllText(Path.Combine(r.JsonOutDir, "connected.g.json"));
        var firstSchema = File.ReadAllText(Path.Combine(r.JsonOutDir, "schema.json"));
        var firstIndex = File.ReadAllText(Path.Combine(r.JsonOutDir, "index.json"));

        var second = r.RunJson();
        second.ExitCode.Should().Be(0);
        File.ReadAllText(Path.Combine(r.JsonOutDir, "connected.g.json")).Should().Be(firstBytes);
        File.ReadAllText(Path.Combine(r.JsonOutDir, "schema.json")).Should().Be(firstSchema);
        File.ReadAllText(Path.Combine(r.JsonOutDir, "index.json")).Should().Be(firstIndex);
    }

    [Fact]
    public void Page_json_contains_expected_machine_and_state()
    {
        using var r = new CodegenRunner();
        r.WriteEventsCatalog(MinimalEvents);
        r.WritePage("data-link/connected.sdl.yaml", ValidMinimalPage);

        var result = r.RunJson();
        result.ExitCode.Should().Be(0);

        var doc = JsonNode.Parse(r.ReadJson("connected.g.json"))!.AsObject();
        doc["kind"]!.GetValue<string>().Should().Be("state");
        doc["machine"]!.GetValue<string>().Should().Be("data_link");
        doc["state"]!.GetValue<string>().Should().Be("Connected");
        doc["source"]!.AsObject()["figure"]!.GetValue<string>().Should().Be("figc.test");

        var transitions = doc["transitions"]!.AsArray();
        transitions.Count.Should().Be(1);
        var t = transitions[0]!.AsObject();
        t["id"]!.GetValue<string>().Should().Be("t01_dl_disconnect_request");
        t["from"]!.GetValue<string>().Should().Be("Connected");
        t["on"]!.GetValue<string>().Should().Be("DL_DISCONNECT_request");
        t["next"]!.GetValue<string>().Should().Be("AwaitingRelease");
        t["actions"]!.AsArray().Count.Should().Be(1);
        t["actions"]![0]!["verb"]!.GetValue<string>().Should().Be("send_disc");
        t["actions"]![0]!["kind"]!.GetValue<string>().Should().Be("signal_lower");
    }

    [Fact]
    public void Index_lists_every_page_with_correct_metadata()
    {
        using var r = new CodegenRunner();
        r.WriteEventsCatalog(MinimalEvents);
        r.WritePage("data-link/connected.sdl.yaml", ValidMinimalPage);
        r.WritePage("data-link/disconnected.sdl.yaml", ValidMinimalPage.Replace(
            "state: Connected", "state: Disconnected", StringComparison.Ordinal));

        var result = r.RunJson();
        result.ExitCode.Should().Be(0);

        var index = JsonNode.Parse(r.ReadJson("index.json"))!.AsObject();
        index["kind"]!.GetValue<string>().Should().Be("index");
        var pages = index["pages"]!.AsArray();
        pages.Count.Should().Be(2);

        // Sorted by filename ordinal (connected before disconnected).
        pages[0]!["file"]!.GetValue<string>().Should().Be("connected.g.json");
        pages[0]!["state"]!.GetValue<string>().Should().Be("Connected");
        pages[1]!["file"]!.GetValue<string>().Should().Be("disconnected.g.json");
        pages[1]!["state"]!.GetValue<string>().Should().Be("Disconnected");
    }

    [Fact]
    public void Schema_rejects_a_state_page_missing_required_fields()
    {
        // Belt-and-braces: confirms the emitted schema is strict enough
        // to catch the kind of regression an emitter bug would produce.
        // If this ever passes a malformed doc, the schema isn't enforcing
        // anything useful.
        using var r = new CodegenRunner();
        r.WriteEventsCatalog(MinimalEvents);
        r.WritePage("data-link/connected.sdl.yaml", ValidMinimalPage);
        r.RunJson().ExitCode.Should().Be(0);

        var schema = JsonSchema.FromText(r.ReadJson("schema.json"))!;
        var malformed = JsonNode.Parse("""
            { "kind": "state", "machine": "data_link" }
            """);

        var eval = schema.Evaluate(malformed, new EvaluationOptions { OutputFormat = OutputFormat.List });
        eval.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Schema_rejects_an_unknown_action_kind()
    {
        using var r = new CodegenRunner();
        r.WriteEventsCatalog(MinimalEvents);
        r.WritePage("data-link/connected.sdl.yaml", ValidMinimalPage);
        r.RunJson().ExitCode.Should().Be(0);

        var schema = JsonSchema.FromText(r.ReadJson("schema.json"))!;
        // Take the real emitted page and corrupt one action's kind.
        var doc = JsonNode.Parse(r.ReadJson("connected.g.json"))!.AsObject();
        doc["transitions"]![0]!["actions"]![0]!["kind"] = "telepathic_emission";

        var eval = schema.Evaluate(doc, new EvaluationOptions { OutputFormat = OutputFormat.List });
        eval.IsValid.Should().BeFalse("'telepathic_emission' is not in the actionKind enum");
    }

    private static string DescribeFailure(EvaluationResults eval)
    {
        var parts = new List<string>();
        Walk(eval, parts);
        return parts.Count == 0 ? "(no details)" : string.Join("; ", parts);

        static void Walk(EvaluationResults r, List<string> parts)
        {
            if (!r.IsValid && r.HasErrors && r.Errors is not null)
            {
                foreach (var e in r.Errors)
                    parts.Add($"{r.InstanceLocation}: {e.Key}={e.Value}");
            }
            if (r.Details is null) return;
            foreach (var d in r.Details) Walk(d, parts);
        }
    }
}
