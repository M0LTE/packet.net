namespace Packet.Ax25.Session;

/// <summary>
/// Evaluates the small boolean expression language used in SDL transition
/// <c>guard:</c> fields. Grammar:
/// <code>
///   expr   := term ("or" term)*
///   term   := factor ("and" factor)*
///   factor := "not"? identifier
/// </code>
/// Identifiers are arbitrary names resolved against an externally-supplied
/// binding table (e.g. <c>"own_receiver_busy"</c> → a lambda reading
/// <c>ctx.OwnReceiverBusy</c>).
/// </summary>
/// <remarks>
/// Whitespace separates tokens. Parentheses are not supported — every
/// guard observed so far in the spec is a simple conjunction of negated
/// or unnegated flags, occasionally an <c>or</c>. We extend the grammar
/// only as new SDL pages need it.
/// </remarks>
public sealed class GuardEvaluator
{
    private static readonly char[] TokenSeparators = { ' ', '\t' };

    private readonly IReadOnlyDictionary<string, Func<bool>> bindings;

    /// <summary>
    /// Build an evaluator over <paramref name="bindings"/>. Each entry maps
    /// an identifier name to a thunk that returns the identifier's current
    /// boolean value. Thunks are evaluated at each <see cref="Evaluate"/>
    /// call, so capturing mutable state via closure is the normal pattern.
    /// </summary>
    public GuardEvaluator(IReadOnlyDictionary<string, Func<bool>> bindings)
    {
        this.bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
    }

    /// <summary>
    /// Evaluate <paramref name="expression"/>. Returns <c>true</c> if the
    /// guard holds. Empty / null / whitespace-only expression is treated as
    /// <c>true</c> (no guard).
    /// </summary>
    /// <exception cref="GuardEvaluationException">
    /// Thrown when the expression references an unbound identifier, or has
    /// a syntax error.
    /// </exception>
    public bool Evaluate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        var tokens = expression.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        int index = 0;
        bool result = ParseOr(tokens, ref index, expression);
        if (index != tokens.Length)
        {
            throw new GuardEvaluationException($"trailing tokens in guard expression '{expression}' at position {index}");
        }
        return result;
    }

    private bool ParseOr(string[] tokens, ref int idx, string original)
    {
        var result = ParseAnd(tokens, ref idx, original);
        while (idx < tokens.Length && tokens[idx] == "or")
        {
            idx++;
            var right = ParseAnd(tokens, ref idx, original);
            result = result || right;
        }
        return result;
    }

    private bool ParseAnd(string[] tokens, ref int idx, string original)
    {
        var result = ParseFactor(tokens, ref idx, original);
        while (idx < tokens.Length && tokens[idx] == "and")
        {
            idx++;
            var right = ParseFactor(tokens, ref idx, original);
            result = result && right;
        }
        return result;
    }

    private bool ParseFactor(string[] tokens, ref int idx, string original)
    {
        if (idx >= tokens.Length)
        {
            throw new GuardEvaluationException($"expected identifier in '{original}' at position {idx}");
        }
        bool negate = false;
        if (tokens[idx] == "not")
        {
            negate = true;
            idx++;
            if (idx >= tokens.Length)
            {
                throw new GuardEvaluationException($"expected identifier after 'not' in '{original}'");
            }
        }
        var ident = tokens[idx++];
        if (!bindings.TryGetValue(ident, out var pred))
        {
            throw new GuardEvaluationException($"unbound identifier '{ident}' in '{original}' — add a binding before evaluating this guard");
        }
        var value = pred();
        return negate ? !value : value;
    }
}

/// <summary>Thrown when a guard expression is malformed or references unbound identifiers.</summary>
public sealed class GuardEvaluationException : Exception
{
    public GuardEvaluationException(string message) : base(message) { }
}
