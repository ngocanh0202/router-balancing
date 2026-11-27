using RouterBalancing.Cli;

namespace RouterBalancing.Cli.Tests;

public class CliArgsTests
{
    [Fact]
    public void Parse_SeparatesPositionalsOptionsAndFlags()
    {
        var args = CliArgs.Parse(["provider", "add", "--name", "Groq", "--no-key", "--base-url=https://x/v1"]);

        Assert.Equal("provider", args.Next());
        Assert.Equal("add", args.Next());
        Assert.Null(args.Next());
        Assert.Equal("Groq", args.Option("name"));
        Assert.Equal("https://x/v1", args.Option("base-url"));
        Assert.True(args.Flag("no-key"));
    }

    [Fact]
    public void Parse_WhenValueOptionIsLastToken_ThrowsUsage()
    {
        Assert.Throws<CliUsageException>(() => CliArgs.Parse(["provider", "add", "--name"]));
    }

    [Fact]
    public void Parse_AfterDoubleDash_TreatsEverythingAsPositional()
    {
        var args = CliArgs.Parse(["model", "add", "p", "--", "--weird-model-id"]);

        Assert.Equal(["model", "add", "p", "--weird-model-id"], args.Remaining);
    }

    [Fact]
    public void OptionList_SplitsCommasAndRepeats()
    {
        var args = CliArgs.Parse(["--models", "a,b", "--models", " c "]);

        Assert.Equal(["a", "b", "c"], args.OptionList("models"));
    }

    [Fact]
    public void OptionValues_KeepsCommasInsideValue()
    {
        var args = CliArgs.Parse(["--header", "Accept=a,b"]);

        Assert.Equal(["Accept=a,b"], args.OptionValues("header"));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("off")]
    [InlineData("0")]
    public void NullableIntOption_ClearWords_ReturnPresentNull(string raw)
    {
        var args = CliArgs.Parse(["--rpm", raw]);

        Assert.Equal((true, (int?)null), args.NullableIntOption("rpm"));
    }

    [Fact]
    public void NullableIntOption_WhenAbsent_ReturnsNotPresent()
    {
        var args = CliArgs.Parse([]);

        Assert.False(args.NullableIntOption("rpm").Present);
    }

    [Fact]
    public void IntOption_WhenNotNumber_ThrowsUsage()
    {
        var args = CliArgs.Parse(["--weight", "abc"]);

        Assert.Throws<CliUsageException>(() => args.IntOption("weight"));
    }

    [Fact]
    public void EnsureOnlyOptions_WhenUnknownOption_ThrowsUsage()
    {
        var args = CliArgs.Parse(["--naem", "x"]);

        var ex = Assert.Throws<CliUsageException>(() => args.EnsureOnlyOptions("name"));
        Assert.Contains("--naem", ex.Message);
    }

    [Fact]
    public void EnsureOnlyOptions_GlobalOptionsAlwaysAllowed()
    {
        var args = CliArgs.Parse(["--json", "--yes", "--db", "x.db"]);

        args.EnsureOnlyOptions();
    }

    [Fact]
    public void EnsureNoMorePositionals_WhenExtraArgument_ThrowsUsage()
    {
        var args = CliArgs.Parse(["list", "extra"]);
        args.Next();

        Assert.Throws<CliUsageException>(args.EnsureNoMorePositionals);
    }

    [Fact]
    public void Parse_ShortHelp_SetsHelpFlag()
    {
        Assert.True(CliArgs.Parse(["provider", "-h"]).Flag("help"));
    }
}
