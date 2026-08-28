namespace Preflight.Cli;

/// <summary>
/// Entry point for the <c>preflight</c> executable.
/// </summary>
/// <remarks>
/// Deliberately thin. Everything worth testing lives in
/// <see cref="PreflightCommandLine"/>, which takes its writers as arguments —
/// a <c>Main</c> that reached for <c>Console</c> directly would push every
/// assertion about output into a test that has to spawn a process.
/// </remarks>
public static class Program
{
    public static int Main(string[] args) =>
        PreflightCommandLine.Execute(args, Console.Out, Console.Error);
}
