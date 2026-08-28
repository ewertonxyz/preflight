namespace Preflight.Core.Policy;

using Preflight.Abstractions.Model;
using Preflight.Abstractions.Services;

/// <summary>
/// Converts a raw parsed policy value (<see langword="bool"/>,
/// <see langword="long"/>, <see langword="string"/>, an array, or an enum
/// name) to the type a caller actually asked for.
/// </summary>
/// <remarks>
/// The user chose to throw rather than fail silently on a type mismatch: a rule
/// asking <see cref="Preflight.Abstractions.Services.IPolicyReader.GetValue{T}"/> for
/// the wrong type and quietly receiving its own fallback is indistinguishable
/// from the key being absent, which is exactly the false green this project
/// exists to prevent. Because a rule runs isolated behind a try/catch, the
/// exception simply makes that rule <c>Errored</c> — the correct status for a
/// defect in the rule itself, not in the workspace.
///
/// Enum values are matched against the exact lowercase strings the schema
/// documents — "error", not "Error" or "ERROR" — because
/// <see cref="Enum.Parse{TEnum}(string)"/> against the PascalCase member name
/// would either wrongly accept the uppercase form (case-insensitive) or wrongly
/// reject the documented lowercase form (case-sensitive against a
/// differently-cased name). Neither is what a strict schema asks for.
/// </remarks>
internal static class PolicyValueConversion
{
    public static T Convert<T>(object? raw)
    {
        if (raw is T typed)
        {
            return typed;
        }

        if (raw is null)
        {
            if (default(T) is null)
            {
                return default!;
            }

            throw new InvalidOperationException($"Expected a value of type {typeof(T)} but found null.");
        }

        if (typeof(T) == typeof(Severity) && raw is string severityText)
        {
            return (T)(object)ParseSeverity(severityText);
        }

        if (raw is long number)
        {
            if (typeof(T) == typeof(int))
            {
                return (T)(object)checked((int)number);
            }
        }

        throw new InvalidOperationException(
            $"Expected a value of type {typeof(T)} but found {raw.GetType()} ('{raw}').");
    }

    private static Severity ParseSeverity(string text) => text switch
    {
        "information" => Severity.Information,
        "warning" => Severity.Warning,
        "error" => Severity.Error,
        _ => throw new InvalidOperationException($"'{text}' is not a valid severity."),
    };
}
