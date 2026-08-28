namespace Preflight.Cli.Tests;

using Preflight.Abstractions.Rules;

/// <summary>
/// Fixes the <c>--set</c> syntax of policy precedence: the two
/// accepted forms, the ambiguity that must fail, and the value typing table.
/// </summary>
/// <remarks>
/// <c>--set</c> sits at the top of the precedence chain, so a defect here does
/// not produce a parse error — it produces a run configured differently from
/// what the user asked for, reported as a success.
/// </remarks>
public sealed class SetOverrideParserTests
{
    private static readonly IReadOnlyList<RuleId> KnownIds =
    [
        new("core.workspace.toolchain"),
        new("core.workspace.dependencies"),
        new("core.presubmit.forbidden-paths"),
        new("core.presubmit.large-file"),
        new("core.build.configuration"),
        new("core.build.compile-probe"),
    ];

    private static Preflight.Core.Policy.PolicySetOverride Parse(string argument) =>
        SetOverrideParser.Parse(argument, KnownIds);

    private static SetOverrideParseException ParseFailure(string argument) =>
        Should.Throw<SetOverrideParseException>(() => SetOverrideParser.Parse(argument, KnownIds));

    [Fact]
    public void Parse_WithTheColonForm_SplitsTheIdFromTheKeyPath()
    {
        var result = Parse("core.presubmit.large-file:settings.maxBytes=1024");

        result.RuleId.ShouldBe(new RuleId("core.presubmit.large-file"));
        result.Path.ShouldBe("settings.maxBytes");
        result.TypedValue.ShouldBe(1024L);
    }

    /// <remarks>
    /// An empty id is not an omission. Policy precedence gives it a meaning:
    /// <c>:maxDegreeOfParallelism=4</c> targets a root key.
    /// </remarks>
    [Fact]
    public void Parse_WithAnEmptyId_TargetsARootKey()
    {
        var result = Parse(":maxDegreeOfParallelism=4");

        result.RuleId.ShouldBeNull();
        result.Path.ShouldBe("maxDegreeOfParallelism");
        result.TypedValue.ShouldBe(4L);
    }

    [Fact]
    public void Parse_WithoutAColon_ResolvesGreedilyAgainstTheDiscoveredIds()
    {
        var result = Parse("core.presubmit.large-file.settings.maxBytes=1024");

        result.RuleId.ShouldBe(new RuleId("core.presubmit.large-file"));
        result.Path.ShouldBe("settings.maxBytes");
    }

    /// <remarks>
    /// The case the colon exists for. Two ids where one is a dotted prefix of
    /// the other make the colon-less form genuinely undecidable, and policy precedence
    /// requires failing with both candidates named rather than picking one —
    /// a silent pick applies an override to a rule the user never named.
    /// </remarks>
    [Fact]
    public void Parse_WhenTwoIdsMatchAsAPrefix_FailsNamingBothCandidates()
    {
        IReadOnlyList<RuleId> overlapping =
        [
            new("core.build.configuration"),
            new("core.build.configuration.extra"),
        ];

        var exception = Should.Throw<SetOverrideParseException>(() =>
            SetOverrideParser.Parse("core.build.configuration.extra.blocking=false", overlapping));

        exception.Message.ShouldContain("core.build.configuration");
        exception.Message.ShouldContain("core.build.configuration.extra");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Parse_TypesABooleanLiteral(string raw, bool expected)
    {
        Parse($"core.presubmit.large-file:blocking={raw}").TypedValue.ShouldBe(expected);
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("-7", -7L)]
    [InlineData("0", 0L)]
    public void Parse_TypesAnIntegerAsLong(string raw, long expected)
    {
        Parse($"core.presubmit.large-file:settings.maxBytes={raw}").TypedValue.ShouldBe(expected);
    }

    private static readonly string[] AbcArray = ["a", "b", "c"];

    [Fact]
    public void Parse_TypesABracketedListAsAStringArray()
    {
        Parse("core.presubmit.forbidden-paths:settings.patterns=[a,b,c]")
            .TypedValue.ShouldBe(AbcArray);
    }

    /// <remarks>
    /// Elements are trimmed. A user typing <c>[a, b]</c> with the space a
    /// human puts after a comma means two patterns, not one pattern and one
    /// beginning with a space — and a leading space in a glob matches nothing,
    /// silently.
    /// </remarks>
    [Fact]
    public void Parse_TrimsWhitespaceAroundListElements()
    {
        Parse("core.presubmit.forbidden-paths:settings.patterns=[a, b, c]")
            .TypedValue.ShouldBe(AbcArray);
    }

    [Fact]
    public void Parse_TypesAnEmptyBracketedListAsAnEmptyArray()
    {
        Parse("core.presubmit.forbidden-paths:settings.patterns=[]")
            .TypedValue.ShouldBe(Array.Empty<string>());
    }

    /// <remarks>
    /// The row that gives the table its escape hatch, and the reason the quoted
    /// form is tested before the boolean one is trusted: without it there would
    /// be no way to set a settings value to the literal text "true".
    /// </remarks>
    [Fact]
    public void Parse_WithAQuotedValue_ForcesAString()
    {
        Parse("core.presubmit.large-file:settings.mode=\"true\"").TypedValue.ShouldBe("true");
    }

    [Fact]
    public void Parse_WithUnrecognisedText_TypesAString()
    {
        Parse("core.presubmit.large-file:settings.mode=hello").TypedValue.ShouldBe("hello");
    }

    /// <remarks>
    /// A settings value is free text, so the split is on the first <c>=</c> and
    /// not the last. Splitting on the last moves part of the value into the key,
    /// which produces an unknown-key error about a key the user never wrote.
    /// </remarks>
    [Fact]
    public void Parse_WithAnEqualsInsideTheValue_SplitsOnTheFirstOne()
    {
        var result = Parse("core.presubmit.large-file:settings.expression=a=b");

        result.Path.ShouldBe("settings.expression");
        result.TypedValue.ShouldBe("a=b");
    }

    [Fact]
    public void Parse_WithAnEmptyValue_TypesAnEmptyString()
    {
        Parse("core.presubmit.large-file:settings.mode=").TypedValue.ShouldBe(string.Empty);
    }

    /// <remarks>
    /// Too large for <c>long</c>, so it falls through to string rather than
    /// throwing. <c>PolicyValidator</c> then reports it as the wrong type for
    /// the key — a message about the value the user typed. An overflow raised
    /// here would be a message about this parser instead.
    /// </remarks>
    [Fact]
    public void Parse_WithAnIntegerTooLargeForLong_FallsThroughToString()
    {
        Parse("core.presubmit.large-file:settings.maxBytes=99999999999999999999")
            .TypedValue.ShouldBe("99999999999999999999");
    }

    [Fact]
    public void Parse_WithoutAnEquals_FailsNamingTheExpectedForm()
    {
        ParseFailure("core.presubmit.large-file:blocking")
            .Message.ShouldContain("<rule-id>:<key>=<value>");
    }

    [Fact]
    public void Parse_WithAnEmptyKeyPath_FailsNamingTheExpectedForm()
    {
        ParseFailure("core.presubmit.large-file:=1")
            .Message.ShouldContain("<rule-id>:<key>=<value>");
    }

    /// <remarks>
    /// The defect this test exists for is not the message, it is the exit code.
    /// <c>RuleId</c> validates in its constructor and throws
    /// <see cref="ArgumentException"/>; uncaught, that leaves the process at
    /// exit 3 with a stack trace — an internal error, claimed for a typo the
    /// user can fix in a second. The exit-code contract makes that difference route an
    /// incident to a different person.
    /// </remarks>
    [Theory]
    [InlineData("Core.Presubmit.Large-File:blocking=false")]
    [InlineData("core.foo:blocking=false")]
    [InlineData("core..large-file:blocking=false")]
    public void Parse_WithAMalformedRuleId_FailsAsAConfigurationError(string argument)
    {
        var exception = ParseFailure(argument);

        exception.ShouldBeAssignableTo<Preflight.Core.ConfigurationLoadException>();
        ExitCode.ForException(exception).ShouldBe(2);
    }

    [Fact]
    public void Parse_WithAnUnknownRuleId_SuggestsTheNearestOne()
    {
        ParseFailure("core.presubmit.large-fil:blocking=false")
            .Message.ShouldContain("core.presubmit.large-file");
    }

    /// <remarks>
    /// Nothing close enough to suggest still has to fail cleanly. Policy validation
    /// asks for a suggestion when there is one, not for a suggestion at any
    /// cost — <c>SuggestionFinder</c> returns nothing above its threshold, and
    /// a message reading "did you mean ''" would be worse than none.
    /// </remarks>
    [Fact]
    public void Parse_WithAnUnknownRuleIdAndNoNearCandidate_FailsWithoutASuggestion()
    {
        var exception = ParseFailure("zzz.yyy.xxx:blocking=false");

        exception.Message.ShouldContain("zzz.yyy.xxx");
        exception.Message.ShouldNotContain("Did you mean");
    }

    [Fact]
    public void Parse_WithoutAColonAndNoMatchingId_FailsAsAnUnknownRuleId()
    {
        ParseFailure("core.presubmit.large-fil.blocking=false")
            .Message.ShouldContain("core.presubmit.large-file");
    }
}
