namespace Preflight.Cli.Interactive;

/// <summary>
/// One row of a picker.
/// </summary>
/// <remarks>
/// <see cref="IsActive"/> and <see cref="IsAllowed"/> are two different facts
/// and neither implies the other. A version can be the one this machine is
/// pinned to and still be outside the range the checkout accepts, which is
/// exactly the state <c>pipeline use</c> exists to get somebody out of; showing
/// it as merely "current" would hide the reason they are looking at this list.
/// </remarks>
/// <param name="Label">What the person reads.</param>
/// <param name="Value">What choosing this row returns.</param>
/// <param name="IsActive">Whether this is what the machine uses today.</param>
/// <param name="IsAllowed">Whether choosing it would produce a workable state.</param>
public sealed record SelectionChoice(string Label, string Value, bool IsActive, bool IsAllowed);
