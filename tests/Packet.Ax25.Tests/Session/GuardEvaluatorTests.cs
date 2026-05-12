using AwesomeAssertions;
using Packet.Ax25.Session;

namespace Packet.Ax25.Tests.Session;

public class GuardEvaluatorTests
{
    private static GuardEvaluator MakeEvaluator(params (string Name, bool Value)[] bindings)
    {
        var dict = bindings.ToDictionary(b => b.Name, b => (Func<bool>)(() => b.Value), StringComparer.Ordinal);
        return new GuardEvaluator(dict);
    }

    [Fact]
    public void Empty_Or_Null_Guard_Is_Trivially_True()
    {
        var evaluator = MakeEvaluator();
        evaluator.Evaluate(null).Should().BeTrue();
        evaluator.Evaluate("").Should().BeTrue();
        evaluator.Evaluate("   ").Should().BeTrue();
    }

    [Theory]
    [InlineData("own_receiver_busy",       true)]
    [InlineData("not own_receiver_busy",   false)]
    public void Identifier_And_Not_Identifier(string expression, bool expected)
    {
        var evaluator = MakeEvaluator(("own_receiver_busy", true));
        evaluator.Evaluate(expression).Should().Be(expected);
    }

    [Theory]
    [InlineData("a and b",         true,  true,  true)]
    [InlineData("a and b",         true,  false, false)]
    [InlineData("a and b",         false, true,  false)]
    [InlineData("a and not b",     true,  false, true)]
    [InlineData("not a and not b", false, false, true)]
    [InlineData("a and b and c",   true,  true,  true)]  // chained
    public void Conjunction(string expression, bool a, bool b, bool expected)
    {
        var evaluator = MakeEvaluator(("a", a), ("b", b), ("c", true));
        evaluator.Evaluate(expression).Should().Be(expected);
    }

    [Theory]
    [InlineData("a or b", true,  false, true)]
    [InlineData("a or b", false, true,  true)]
    [InlineData("a or b", false, false, false)]
    public void Disjunction(string expression, bool a, bool b, bool expected)
    {
        var evaluator = MakeEvaluator(("a", a), ("b", b));
        evaluator.Evaluate(expression).Should().Be(expected);
    }

    [Fact]
    public void Unbound_Identifier_Throws()
    {
        var evaluator = MakeEvaluator(("a", true));
        var act = () => evaluator.Evaluate("a and missing_flag");
        act.Should().Throw<GuardEvaluationException>()
           .WithMessage("*missing_flag*");
    }

    [Fact]
    public void Trailing_Garbage_Throws()
    {
        var evaluator = MakeEvaluator(("a", true), ("b", true));
        var act = () => evaluator.Evaluate("a b");  // missing 'and'/'or'
        act.Should().Throw<GuardEvaluationException>();
    }

    [Fact]
    public void Bare_Not_Throws()
    {
        var evaluator = MakeEvaluator(("a", true));
        var act = () => evaluator.Evaluate("not");
        act.Should().Throw<GuardEvaluationException>();
    }

    [Fact]
    public void Bindings_Are_Re_Evaluated_Each_Call()
    {
        // Closures that look at mutable state must reflect changes between
        // evaluations — guards are checked at dispatch time, not at
        // binding-construction time.
        bool busy = false;
        var bindings = new Dictionary<string, Func<bool>>
        {
            ["own_receiver_busy"] = () => busy,
        };
        var evaluator = new GuardEvaluator(bindings);

        evaluator.Evaluate("own_receiver_busy").Should().BeFalse();
        busy = true;
        evaluator.Evaluate("own_receiver_busy").Should().BeTrue();
    }

    [Fact]
    public void Real_World_Guards_From_Connected_Transcription()
    {
        // The exact guard strings used in figc4.4a cols 5+6 (currently
        // transcribed) — sanity-check the grammar covers them.
        var bindings = new Dictionary<string, Func<bool>>
        {
            ["own_receiver_busy"] = () => true,
            ["T1_running"]        = () => false,
        };
        var evaluator = new GuardEvaluator(bindings);

        evaluator.Evaluate("own_receiver_busy").Should().BeTrue();
        evaluator.Evaluate("not own_receiver_busy").Should().BeFalse();
        evaluator.Evaluate("own_receiver_busy and not T1_running").Should().BeTrue();
        evaluator.Evaluate("own_receiver_busy and T1_running").Should().BeFalse();
    }
}
