namespace Preflight.Rules;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Preflight.Abstractions;

/// <summary>
/// Compiles without linking and reports whatever the compiler said.
/// </summary>
/// <remarks>
/// <para>
/// The expensive rule of the six, and the one that justifies everything else:
/// it runs only if the two before it passed. That is the argument for a
/// dependency graph, in the form of a rule.
/// </para>
/// <para>
/// It is the reason <see cref="IProcessRunner"/> exists — the rule that most
/// needs to be testable without invoking a real compiler. Every unit test here
/// does exactly that.
/// </para>
/// </remarks>
public sealed class CompileProbeRule : IValidationRule, ICacheableRule
{
    /// <summary>
    /// The token a manifest puts where the probe should write.
    /// </summary>
    public const string OutputToken = "{probeOutput}";

    /// <remarks>
    /// Two forms, because the compilers a production actually uses do not
    /// agree. MSBuild and the C# compiler write
    /// <c>path(line,col): error CS1002: text</c>; clang, gcc and most of the
    /// Unix world write <c>path:line:col: error: text</c>. Supporting one would
    /// mean the findings of half the world's compilers arriving as a single
    /// blob of text with no location on it.
    /// </remarks>
    private static readonly Regex MsBuildDiagnostic = new(
        @"^(?<path>[^(\r\n]+)\((?<line>\d+)(,(?<column>\d+))?\)\s*:\s*(error|fatal error)\s*(?<code>[^:]*):\s*(?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex UnixDiagnostic = new(
        @"^(?<path>[^:\r\n]+):(?<line>\d+):((?<column>\d+):)?\s*(error|fatal error)\s*:\s*(?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public RuleDescriptor Descriptor { get; } = new()
    {
        Id = BuiltInRuleIds.CompileProbe,
        DisplayName = "Compile probe",
        Stage = ValidationStage.BuildReadiness,
        DependsOn = [BuiltInRuleIds.BuildConfiguration],
        DefaultBlocking = true,

        // False on a leaf. Nothing depends on this rule — it is the
        // end of the chain, which is the whole point of it being last.
        DefaultGating = false,
    };

    public async Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var manifestPath = Path.Combine(
            context.WorkspaceRoot.FullName,
            context.Policy.GetValue("manifestPath", WorkspaceManifest.DefaultFileName));

        WorkspaceManifest? manifest;

        try
        {
            manifest = await WorkspaceManifest.LoadAsync(context.FileSystem, manifestPath, cancellationToken);
        }
        catch (JsonException exception)
        {
            return RuleOutcome.Failed(new Finding
            {
                Message = "The workspace manifest is not valid JSON.",
                Location = new FindingLocation(manifestPath),
                Actual = exception.Message,
                Remediation = "Fix the syntax, or point 'manifestPath' at the right file in the policy.",
            });
        }

        // No manifest, or no probe declared in it, means nobody told this rule
        // how to compile the workspace. The NotApplicable reasoning
        // applies unchanged: it examined nothing, and core.workspace.toolchain
        // already fails loudly on a manifest that should be there and is not.
        if (manifest?.CompileProbe is not { } probe)
        {
            return RuleOutcome.NotApplicable();
        }

        var output = Path.Combine(Path.GetTempPath(), "preflight-probe", Guid.NewGuid().ToString("N"));

        var result = await context.Processes.RunAsync(
            new ProcessRequest
            {
                FileName = probe.Command,

                // The token is substituted, never appended. A rule that added
                // an output argument of its own would be guessing at the
                // compiler's flag syntax, and guessing wrong turns a probe into
                // a build that writes into the workspace — the non-objective
                // this token exists to serve.
                Arguments = [.. probe.Arguments.Select(argument =>
                    argument.Replace(OutputToken, output, StringComparison.Ordinal))],
                WorkingDirectory = probe.WorkingDirectory is { } relative
                    ? Path.Combine(context.WorkspaceRoot.FullName, relative)
                    : context.WorkspaceRoot.FullName,
            },
            cancellationToken);

        if (result.ExitCode == 0)
        {
            return RuleOutcome.Passed();
        }

        var findings = Diagnostics(result, cancellationToken);

        return RuleOutcome.Failed([.. findings]);
    }

    /// <summary>
    /// Describes the probe's inputs, when the manifest declares them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This rule is the reason the cache exists: fifteen seconds, re-run over
    /// sources that did not change. It is also the rule that cannot work out
    /// its own inputs, because what it reads is whatever the compiler reads,
    /// and the compiler is a child process this rule never looks inside. So it
    /// reads the declaration in the manifest, and returns
    /// <see langword="null"/> when there is none — which is the default, and the
    /// safe one.
    /// </para>
    /// <para>
    /// Content, never a timestamp. An mtime-based fingerprint is the classic
    /// approximation, and it is wrong in both directions: a checkout restores
    /// content and changes every timestamp, and a file written twice in the
    /// same tick changes content and keeps one. There is no approximate
    /// fingerprint.
    /// </para>
    /// <para>
    /// The probe's own command line is part of the fingerprint. Changing a
    /// compiler flag changes the answer without changing one byte of the
    /// sources, and it is the same class of mistake as leaving the effective
    /// policy out of the key.
    /// </para>
    /// </remarks>
    public async Task<CacheFingerprint?> ComputeFingerprintAsync(
        RuleContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var manifestPath = Path.Combine(
            context.WorkspaceRoot.FullName,
            context.Policy.GetValue("manifestPath", WorkspaceManifest.DefaultFileName));

        WorkspaceManifest? manifest;

        try
        {
            manifest = await WorkspaceManifest.LoadAsync(context.FileSystem, manifestPath, cancellationToken);
        }
        catch (JsonException)
        {
            // A manifest that will not parse is a Failed outcome the rule itself
            // reports. Declining to describe inputs is the honest answer here:
            // there is nothing to describe, and the failure is not cacheable
            // anyway once the file is fixed.
            return null;
        }

        if (manifest?.CompileProbe is not { Inputs: { Count: > 0 } inputs } probe)
        {
            return null;
        }

        var builder = new StringBuilder();

        builder.Append(probe.Command).Append('\u001f')
            .Append(string.Join('\u001f', probe.Arguments)).Append('\u001f')
            .Append(probe.WorkingDirectory ?? string.Empty).Append('\u001e');

        foreach (var input in inputs.Order(StringComparer.Ordinal))
        {
            await DescribeAsync(context, input, builder, cancellationToken);
        }

        return new CacheFingerprint(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))));
    }

    /// <summary>
    /// Appends one declared input to the fingerprint.
    /// </summary>
    /// <remarks>
    /// A path that is not there is described as absent rather than skipped. A
    /// directory that appears between two runs changes what the compiler sees,
    /// and a fingerprint that ignored its absence would keep serving the result
    /// from before it existed.
    /// </remarks>
    private static async Task DescribeAsync(
        RuleContext context,
        string input,
        StringBuilder builder,
        CancellationToken cancellationToken)
    {
        var full = Path.Combine(context.WorkspaceRoot.FullName, input);

        builder.Append(input).Append('\u001f');

        if (context.FileSystem.FileExists(full))
        {
            await DescribeFileAsync(context, full, builder, cancellationToken);

            return;
        }

        if (!context.FileSystem.DirectoryExists(full))
        {
            builder.Append("absent").Append('\u001e');

            return;
        }

        // Ordinal, because EnumerateFiles promises no order and a fingerprint
        // that depended on it would differ between two identical workspaces.
        foreach (var file in context.FileSystem
                     .EnumerateFiles(full, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            builder.Append(Relative(context, file)).Append('\u001f');

            await DescribeFileAsync(context, file, builder, cancellationToken);
        }
    }

    private static async Task DescribeFileAsync(
        RuleContext context,
        string path,
        StringBuilder builder,
        CancellationToken cancellationToken)
    {
        var bytes = await context.FileSystem.ReadAllBytesAsync(path, cancellationToken);

        builder.Append(Convert.ToHexStringLower(SHA256.HashData(bytes))).Append('\u001e');
    }

    /// <remarks>
    /// Relative and with forward slashes, so a fingerprint taken on Windows
    /// equals the one taken on a build agent that checked the same commit out
    /// on Linux. An absolute path would make the cache miss on every machine
    /// but the one that filled it.
    /// </remarks>
    private static string Relative(RuleContext context, string path) =>
        Path.GetRelativePath(context.WorkspaceRoot.FullName, path).Replace('\\', '/');

    /// <summary>
    /// Turns compiler output into one finding per diagnostic.
    /// </summary>
    /// <remarks>
    /// Both streams are read, because compilers disagree about which one
    /// diagnostics belong on: MSBuild writes them to standard output, clang to
    /// standard error. Reading one would silently lose every diagnostic from
    /// half the toolchains.
    /// </remarks>
    public static List<Finding> Diagnostics(ProcessResult result, CancellationToken cancellationToken)
    {
        var findings = new List<Finding>();

        foreach (var line in Lines(result.StandardOutput).Concat(Lines(result.StandardError)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Describe(line) is { } finding)
            {
                findings.Add(finding);
            }
        }

        if (findings.Count == 0)
        {
            // The compiler failed and said nothing this parser recognised.
            // Reporting no findings would produce a failure with no evidence,
            // which is the one thing a failing rule must not do.
            findings.Add(new Finding
            {
                Message = "The compile probe failed.",
                Actual = Summarise(result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput),
                Remediation = "Run the probe command by hand to see the full output.",
            });
        }

        return findings;
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r'));

    private static Finding? Describe(string line)
    {
        var match = MsBuildDiagnostic.Match(line);

        if (!match.Success)
        {
            match = UnixDiagnostic.Match(line);
        }

        if (!match.Success)
        {
            return null;
        }

        var code = match.Groups["code"].Success ? match.Groups["code"].Value.Trim() : string.Empty;

        return new Finding
        {
            Message = code.Length > 0
                ? $"{code}: {match.Groups["message"].Value.Trim()}"
                : match.Groups["message"].Value.Trim(),
            Location = new FindingLocation(
                match.Groups["path"].Value.Trim().Replace('\\', '/'),
                int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture),
                // Success alone. A '\d+' group that matched cannot be empty, so
                // a length check beside it is a condition no input can make
                // false — a permanent hole in the branch count dressed as
                // caution.
                match.Groups["column"].Success
                    ? int.Parse(match.Groups["column"].Value, CultureInfo.InvariantCulture)
                    : null),
            Remediation = "Fix the compile error.",
        };
    }

    /// <remarks>
    /// Capped for the reason a history record is capped at 64 KB: a compiler
    /// that prints its whole help on a bad argument would otherwise put all of
    /// it in the console report and in every stored line.
    /// </remarks>
    private static string Summarise(string text)
    {
        var trimmed = text.Trim();

        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "…";
    }
}
