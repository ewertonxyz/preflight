namespace Preflight.Cli.Commands;

using Preflight.Abstractions;
using Preflight.Core;
using Preflight.Rules;

/// <summary>
/// Raised when the file <c>create</c> would write already exists.
/// </summary>
/// <remarks>
/// A configuration error, so it exits 2 through the one mapping that decides
/// exit codes. 3 would say the tool broke; the tool did exactly what it
/// promised.
/// </remarks>
public sealed class WorkspaceFileExistsException : ConfigurationLoadException
{
    public WorkspaceFileExistsException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// <c>preflight create workspace</c>, <c>create rule</c> and <c>create policy</c>.
/// </summary>
/// <remarks>
/// <c>Docs/design.md 9.1</c> defines the workspace manifest; ADR-028 records why
/// a command may write into the workspace when a run may not. The three
/// subcommands share one shape — decide the path, refuse an occupant, write a
/// commented skeleton — and the sharing is deliberate: a scaffold that behaved
/// differently from its neighbour would be a second set of promises to learn.
/// </remarks>
public static class CreateCommandHandler
{
    /// <summary>
    /// Writes a plugin project for one rule, or refuses because something is
    /// already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two files: a project and a class. The project is the point. Its single
    /// <c>ProjectReference</c> carries <c>Private="false"</c>, which is the one
    /// line the whole plugin model rests on and the one a plugin author gets
    /// wrong by doing nothing — the default copies
    /// <c>Preflight.Abstractions.dll</c> beside the plugin, the load context
    /// finds that copy, and the interface the rule implements stops being the
    /// interface the engine knows. Every plugin written from a wrong scaffold
    /// inherits that bug, which is why the same assertion guards this and the
    /// worked sample.
    /// </para>
    /// <para>
    /// The layout is derived from the id rather than asked about: <c>a.b.c</c>
    /// becomes <c>A.B.C/</c> holding <c>A.B.C.csproj</c> and <c>CRule.cs</c>.
    /// One derivation nobody has to remember beats a second argument nobody
    /// wants to supply, and the id is already the primary key of the thing being
    /// created.
    /// </para>
    /// </remarks>
    /// <param name="environment">Where the workspace and the writer are.</param>
    /// <param name="id">The rule id, which decides every name.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<int> RuleAsync(
        CommandEnvironment environment,
        string id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(id);

        // Translated rather than allowed to escape. RuleId raises
        // ArgumentException, which is not a ConfigurationLoadException and
        // therefore leaves as exit 3 — the code that says this tool broke,
        // about a typo somebody just made on the command line.
        RuleId ruleId;

        try
        {
            ruleId = new RuleId(id);
        }
        catch (ArgumentException exception)
        {
            throw new WorkspaceFileExistsException(WithoutTheParameterName(exception));
        }

        var segments = ruleId.Value.Split('.');
        var project = string.Join('.', segments.Select(Pascal));
        var className = $"{Pascal(segments[^1])}Rule";

        var directory = Path.Combine(environment.WorkspaceRoot.FullName, project);
        var projectPath = Path.Combine(directory, $"{project}.csproj");
        var sourcePath = Path.Combine(directory, $"{className}.cs");

        // Both checked before either is written. Half a scaffold is worse than
        // none: it compiles to nothing, and the second attempt refuses because
        // of the file the first one left behind.
        foreach (var path in new[] { projectPath, sourcePath })
        {
            if (environment.WorkspaceWriter.Exists(path))
            {
                throw new WorkspaceFileExistsException(
                    $"'{Path.GetFileName(path)}' already exists at {path}. " +
                    "Move it aside first; this command never replaces one.");
            }
        }

        Directory.CreateDirectory(directory);

        await WriteAsync(environment, projectPath, RuleProject, cancellationToken);
        await WriteAsync(
            environment, sourcePath, RuleSource(ruleId, project, className), cancellationToken);

        environment.Console.Output.WriteLine($"Wrote {project}/{project}.csproj and {className}.cs.");
        environment.Console.Output.WriteLine(
            "Point the ProjectReference at your copy of Preflight.Abstractions, build it, then run: " +
            $"preflight rules --rules-path {project}/bin/Debug/net10.0");

        return ExitCode.Success;
    }

    /// <summary>
    /// Writes a named pipeline's policy document, or refuses because one is
    /// already there.
    /// </summary>
    /// <remarks>
    /// The name becomes a file name, so it is held to the label rule first and
    /// then to a second list. <c>base</c>, <c>local</c> and <c>workspace</c> are
    /// refused because <c>preflight.base.json</c>, <c>preflight.local.json</c>
    /// and <c>preflight.workspace.json</c> are not pipeline overlays at all —
    /// they are the same three the pipeline selector already refuses to treat as
    /// pipelines, and <c>create policy base</c> would otherwise write the file
    /// <c>pipeline declare</c> owns.
    /// </remarks>
    /// <param name="environment">Where the workspace and the writer are.</param>
    /// <param name="name">The pipeline this overlay configures.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<int> PolicyAsync(
        CommandEnvironment environment,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(name);

        PipelineName.Require(name);

        var fileName = PolicyResolution.PipelineFileName(name);

        if (PipelineSelector.ReservedFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
        {
            throw new WorkspaceFileExistsException(
                $"'{name}' does not name a pipeline: '{fileName}' is one of this tool's own files. " +
                "Name the pipeline after the game it validates.");
        }

        var path = Path.Combine(environment.WorkspaceRoot.FullName, fileName);

        if (environment.WorkspaceWriter.Exists(path))
        {
            throw new WorkspaceFileExistsException(
                $"'{fileName}' already exists at {path}. " +
                "Edit it, or move it aside first; this command never replaces one.");
        }

        await WriteAsync(environment, path, PolicySkeleton(name), cancellationToken);

        environment.Console.Output.WriteLine($"Wrote {fileName}.");
        environment.Console.Output.WriteLine(
            $"Tighten what this pipeline needs, then run: preflight rules --pipeline {name}");

        return ExitCode.Success;
    }

    /// <remarks>
    /// One translation site for all three scaffolds. An <see cref="IOException"/>
    /// reaching the top is exit 3, which says this tool broke; a full disk or a
    /// read-only directory is the workspace's condition, and 2 is the code that
    /// sends the right person to look.
    /// </remarks>
    private static async Task WriteAsync(
        CommandEnvironment environment,
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        try
        {
            await environment.WorkspaceWriter.WriteNewAsync(path, content, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceFileExistsException(
                $"Could not write '{Path.GetFileName(path)}' at {path}: {exception.Message}");
        }
    }

    /// <remarks>
    /// <see cref="ArgumentException.Message"/> appends "(Parameter 'value')",
    /// which is a fact about this codebase's method signature and means nothing
    /// to somebody who mistyped a rule id. The sentence in front of it is the
    /// one worth printing, and it is <see cref="RuleId"/>'s own — reworded here
    /// it would be a second wording of the same rule, drifting from the first.
    /// </remarks>
    private static string WithoutTheParameterName(ArgumentException exception) =>
        exception.Message.Replace(
            $" (Parameter '{exception.ParamName}')", string.Empty, StringComparison.Ordinal);

    private static string Pascal(string segment) =>
        string.Concat(segment.Split('-').Select(part => part.Length == 0
            ? part
            : char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>What the generated project file contains.</summary>
    /// <remarks>
    /// The comment inside it is longer than the markup, and stays. That single
    /// attribute is the one a plugin author is most likely to delete while
    /// tidying, and the failure it causes — an <c>IValidationRule</c> that is
    /// not the engine's <c>IValidationRule</c> — reads like nothing at all.
    /// </remarks>
    public static string RuleProject { get; } =
        """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
          </PropertyGroup>

          <ItemGroup>
            <!--
              Private="false" is the line this whole file exists for. The default
              copies Preflight.Abstractions.dll into the output; the plugin then
              ships its own copy of the contract, the engine's load context finds
              that copy sitting beside the plugin, and the IValidationRule this
              rule implements is a different type from the one the engine knows.
              The rule is then silently not a rule.

              Point Include at wherever Preflight.Abstractions lives for you: a
              checkout, or the published contract. Nothing else belongs in this
              project — a rule sees the contracts and the BCL, and the engine has
              a test that says so.
            -->
            <ProjectReference Include="..\Preflight.Abstractions\Preflight.Abstractions.csproj"
                              Private="false" />
          </ItemGroup>

        </Project>

        """;

    private static string RuleSource(RuleId id, string projectNamespace, string className) =>
        $$"""
        namespace {{projectNamespace}};

        using Preflight.Abstractions;

        /// <summary>
        /// TODO: what this rule checks, in one sentence a content author would
        /// recognise.
        /// </summary>
        public sealed class {{className}} : IValidationRule
        {
            public RuleDescriptor Descriptor { get; } = new()
            {
                Id = new RuleId("{{id}}"),
                DisplayName = "TODO",
                Stage = ValidationStage.Workspace,

                // Stated rather than left to the descriptor's defaults, so that
                // a default does not read as a decision somebody made.
                DefaultSeverity = Severity.Error,
                DefaultBlocking = true,
                DefaultGating = false,
            };

            /// <remarks>
            /// Everything this reads comes through <c>context</c>: the file
            /// system, the process runner, the logger. Reaching for System.IO or
            /// Process directly makes the rule untestable, and makes its verdict
            /// depend on the machine rather than on the workspace.
            /// </remarks>
            public Task<RuleOutcome> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(context);

                // "There was nothing of this kind to look at" is NotApplicable,
                // never Passed. A tick claims a check that never happened, and
                // that is the one defect this whole tool exists to refuse.
                //
                // A Finding carries what was expected, what was found and what to
                // do about it. All three, always: a message that says only
                // "invalid" sends somebody to ask a colleague.
                return Task.FromResult(RuleOutcome.Passed());
            }
        }

        """;

    /// <remarks>
    /// Commented, and empty of decisions. The skeleton shows the shape and names
    /// the keys; what the limits should be is the studio's call, and a file that
    /// arrived with plausible numbers in it is a file nobody reads before
    /// trusting — the argument ADR-023 already made about the workspace
    /// manifest.
    /// </remarks>
    private static string PolicySkeleton(string name) =>
        $$"""
        {
          "schemaVersion": 1,

          // The '{{name}}' pipeline's policy. Everything here overrides a rule
          // descriptor's own default and nothing else: a key absent from this
          // file is the engine's default, not a zero.

          // Comments and trailing commas are legal. Say why a limit is what it
          // is, beside the limit — the next person to raise it is the one who
          // needs the reason.

          "rules": {
            // "core.presubmit.large-file": {
            //   "enabled": true,
            //   "blocking": true,
            //   "settings": { "maximumBytes": 10485760 }
            // }
          }

          // Per-platform and per-configuration overrides, for when one number
          // does not fit every target:
          //
          // "targets": {
          //   "platform:switch": {
          //     "rules": {
          //       "core.presubmit.large-file": { "settings": { "maximumBytes": 5242880 } }
          //     }
          //   }
          // }

          // Keys a downstream layer may not override. A sealed key is a limit
          // the studio decided once:
          //
          // "sealed": ["core.presubmit.large-file:blocking"]
        }

        """;

    /// <summary>
    /// Writes the manifest skeleton, or refuses because one is already there.
    /// </summary>
    /// <remarks>
    /// The existence check produces the message; the writer's refusal to replace
    /// is what makes the promise true. Between the two another process can
    /// create the file, and only the second one notices.
    /// </remarks>
    public static async Task<int> WorkspaceAsync(
        CommandEnvironment environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var path = Path.Combine(environment.WorkspaceRoot.FullName, WorkspaceManifest.DefaultFileName);

        if (environment.WorkspaceWriter.Exists(path))
        {
            throw new WorkspaceFileExistsException(
                $"'{WorkspaceManifest.DefaultFileName}' already exists at {path}. " +
                "Edit it, or move it aside first; this command never replaces one.");
        }

        try
        {
            await environment.WorkspaceWriter.WriteNewAsync(path, Skeleton, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Translated rather than allowed to escape. An IOException reaching
            // the top is exit 3, which says this tool broke; a full disk or a
            // read-only directory is the workspace's condition, not a defect
            // here, and 2 is the code that sends the right person to look.
            throw new WorkspaceFileExistsException(
                $"Could not write '{WorkspaceManifest.DefaultFileName}' at {path}: {exception.Message}");
        }

        environment.Console.Output.WriteLine($"Wrote {WorkspaceManifest.DefaultFileName}.");
        environment.Console.Output.WriteLine("Declare the tools this workspace needs, then run: preflight run --stage workspace");

        return ExitCode.Success;
    }

    /// <summary>
    /// What the generated file contains.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty of facts, and deliberately so. ADR-023 refused inference once
    /// already, and every argument it made applies here with one addition: a
    /// manifest that arrived pre-filled is a manifest nobody reads before
    /// trusting, and this file is the one place a workspace states what it
    /// needs.
    /// </para>
    /// <para>
    /// Two empty arrays rather than an empty object, because the difference
    /// matters to the rules that read it: a manifest declaring no tools is
    /// somebody saying in writing that there is nothing to check, and both
    /// workspace rules answer <c>n/a</c>. A missing manifest fails
    /// (<c>Docs/design.md 9.1</c>), and this command exists to move a new
    /// project from the second state to the first.
    /// </para>
    /// <para>
    /// Comments and trailing commas are legal here — <c>WorkspaceManifest</c>
    /// grants them for the same reason the policy schema does — and the
    /// commented examples are the documentation the user reads at the moment
    /// they need it.
    /// </para>
    /// </remarks>
    public static string Skeleton { get; } =
        """
        {
          // What this workspace needs in order to build. Nothing here was
          // detected: the tool never guesses what a project uses, because a
          // manifest that arrives pre-filled is one nobody checks.
          //
          // Every tool listed is run and its version compared. minimumVersion is
          // inclusive, maximumVersion is exclusive — "anything in 10.x" is
          // written "10.0.0" to "11.0.0".
          //
          //   {
          //     "name": "MSVC",
          //     "command": "cl",
          //     "arguments": ["/help"],
          //     "minimumVersion": "19.0.0",
          //     "maximumVersion": "20.0.0"
          //   }
          "tools": [],

          // What must be on disk after a restore. There is no network lookup:
          // restoredMarker is a path relative to this file, and whether it
          // exists is the whole check.
          //
          //   { "id": "Serilog", "version": "3.1.1", "restoredMarker": "packages/serilog" }
          "dependencies": []

          // Optional. How to compile-probe this workspace.
          //
          // {probeOutput} is replaced with a path outside the workspace,
          // because a run never writes here. "inputs" lists everything the
          // probe reads; leaving it out means the probe is never cached, which
          // is the safe default — an incomplete list serves a cached pass after
          // a change it did not know about.
          //
          // "compileProbe": {
          //   "command": "dotnet",
          //   "arguments": ["build", "--no-restore", "-p:OutputPath={probeOutput}"],
          //   "workingDirectory": "src",
          //   "inputs": ["src", "Directory.Build.props"]
          // }
        }

        """;
}
