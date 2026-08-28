namespace Preflight.TestSupport;

using Preflight.Abstractions.Rules;

/// <summary>
/// The contract version a fixture writes when it means "a package this build
/// accepts".
/// </summary>
/// <remarks>
/// Read from the running assembly rather than typed as a literal. Nine package
/// fixtures used to spell the number by hand, and the first bump of the
/// contract turned all nine into failures that named a version rather than the
/// mistake — the fixtures had gone stale, and somebody had to work that out
/// nine times over. Derived here, a bump costs nothing.
///
/// A fixture that means "a package this build refuses" states its own number
/// instead. There the value is the point of the test, and deriving it would
/// erase what the test is checking.
/// </remarks>
public static class ContractVersion
{
    /// <summary>The three-part version this build of the contract provides.</summary>
    public static string Current { get; } =
        typeof(IValidationRule).Assembly.GetName().Version!.ToString(3);
}
