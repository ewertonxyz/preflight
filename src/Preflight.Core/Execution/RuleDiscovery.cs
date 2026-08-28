namespace Preflight.Core;

using System.Reflection;
using Preflight.Abstractions.Rules;

/// <summary>
/// Thrown when a type that declared itself a rule cannot be turned into one.
/// </summary>
/// <remarks>
/// A <see cref="ConfigurationLoadException"/> rather than a run outcome, on
/// purpose. A load failure is exit 2, and the distinction is worth keeping: a
/// broken configuration calls the tool's owner, a failing check calls the
/// commit author.
/// </remarks>
public sealed class RuleDiscoveryException : ConfigurationLoadException
{
    public RuleDiscoveryException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Finds the rules in a set of types or assemblies.
/// </summary>
/// <remarks>
/// <para>
/// A rule needs a public parameterless constructor, and the engine instantiates
/// it with <see cref="Activator"/> — no dependency injection container is
/// involved, and services reach the rule through its context.
/// </para>
/// <para>
/// The types to scan are supplied by the caller rather than resolved here.
/// <c>Preflight.Core</c> must never reference <c>Preflight.Rules</c>, so "the
/// internal assembly" cannot be named from inside the engine; the CLI passes it
/// in. That constraint pays for itself in testability, since a test assembly is
/// an assembly.
/// </para>
/// </remarks>
public static class RuleDiscovery
{
    public static IReadOnlyList<IValidationRule> FromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return FromTypes([.. assemblies.SelectMany(assembly => assembly.GetTypes())]);
    }

    public static IReadOnlyList<IValidationRule> FromTypes(IReadOnlyList<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var rules = new List<IValidationRule>();

        foreach (var type in types)
        {
            if (!IsCandidate(type))
            {
                continue;
            }

            rules.Add(Instantiate(type));
        }

        // Ordered because Assembly.GetTypes guarantees nothing between builds,
        // and a run whose very first step varies is not reproducible at all.
        return [.. rules.OrderBy(rule => rule.Descriptor.Id.Value, StringComparer.Ordinal)];
    }

    /// <remarks>
    /// Only <c>IsAbstract</c> is tested, not <c>IsAbstract || IsInterface</c>:
    /// an interface is already abstract, so the second half would be a branch
    /// no input can reach.
    ///
    /// A type that is not visible outside its assembly is not a candidate at
    /// all — it was never offered. <c>IsVisible</c> rather than <c>IsPublic</c>
    /// because the latter is false for a public type nested in another type,
    /// which would quietly disqualify a perfectly reachable rule.
    ///
    /// Ignoring an invisible type hides nothing: if a policy enables its id,
    /// the policy validator rejects the file with "unknown rule id" and a
    /// suggestion, which is the layer that can explain it.
    /// </remarks>
    private static bool IsCandidate(Type type) =>
        typeof(IValidationRule).IsAssignableFrom(type) &&
        !type.IsAbstract &&
        !type.IsGenericTypeDefinition &&
        type.IsVisible;

    /// <remarks>
    /// A public type that writes <c>: IValidationRule</c> has declared an
    /// intent the engine is obliged to honour or to reject out loud. Dropping
    /// it silently would leave the rule missing from a green report, which is
    /// the false green principle 7 forbids.
    /// </remarks>
    private static IValidationRule Instantiate(Type type)
    {
        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new RuleDiscoveryException(
                $"Rule type '{type.FullName}' has no public parameterless constructor. " +
                "The engine instantiates rules by reflection, without a container.");
        }

        IValidationRule rule;

        try
        {
            rule = (IValidationRule)Activator.CreateInstance(type)!;
        }
        catch (TargetInvocationException exception)
        {
            // Unwrapped, because the wrapper says only that reflection was
            // involved, and the message a human needs is underneath it.
            // GetBaseException rather than InnerException: it never returns
            // null, so there is no fallback branch that no input could reach.
            throw new RuleDiscoveryException(
                $"Rule type '{type.FullName}' threw while being constructed: " +
                exception.GetBaseException().Message);
        }

        RequireAReadableDescriptor(rule, type);

        return rule;
    }

    /// <summary>
    /// Reads the descriptor once, here, where a failure can be explained.
    /// </summary>
    /// <remarks>
    /// The ordering below is the first thing that touches
    /// <c>rule.Descriptor</c>, and it is outside every <c>try</c> in this file.
    /// A rule whose descriptor is a computed property rather than an
    /// initialised one — which is legal, and which a plugin author writing
    /// <c>public RuleDescriptor Descriptor =&gt; Build();</c> arrives at
    /// without trying — therefore left the process on exit 3 with a stack
    /// trace, claiming an internal error for a defect in somebody's rule — the
    /// difference between calling the tool's owner and calling the rule's
    /// author.
    ///
    /// It is read rather than merely guarded: the value is discarded, and what
    /// the call buys is that the throw happens where the type's name is still
    /// in hand.
    /// </remarks>
    private static void RequireAReadableDescriptor(IValidationRule rule, Type type)
    {
        try
        {
            _ = rule.Descriptor.Id;
        }
        catch (Exception exception)
        {
            throw new RuleDiscoveryException(
                $"Rule type '{type.FullName}' threw while its descriptor was read: " +
                exception.Message);
        }
    }
}
