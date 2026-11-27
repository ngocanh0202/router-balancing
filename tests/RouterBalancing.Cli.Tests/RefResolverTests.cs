using RouterBalancing.Cli;
using RouterBalancing.Core.Domain;

namespace RouterBalancing.Cli.Tests;

public class RefResolverTests
{
    private static List<Provider> Providers()
    {
        var openRouter = new Provider { Id = 1, Name = "OpenRouter", Identifier = "openrouter" };
        openRouter.Models.Add(new Model { Id = 10, ProviderId = 1, ModelId = "meta-llama/llama-3.1-8b:free" });
        openRouter.Accounts.Add(new ProviderAccount { Id = 50, ProviderId = 1, Name = "Default" });
        var groq = new Provider { Id = 2, Name = "Groq", Identifier = "groq" };
        groq.Models.Add(new Model { Id = 20, ProviderId = 2, ModelId = "llama-3.1-8b" });
        return [openRouter, groq];
    }

    [Theory]
    [InlineData("2")]
    [InlineData("groq")]
    [InlineData("GROQ")]
    public void Provider_ResolvesByIdIdentifierOrName(string reference)
    {
        Assert.Equal(2, RefResolver.Provider(Providers(), reference).Id);
    }

    [Fact]
    public void Provider_IdentifierWinsOverName()
    {
        var providers = Providers();
        // Provider 2 có tên trùng identifier của provider 1 — identifier phải thắng
        providers[1].Name = "openrouter";

        Assert.Equal(1, RefResolver.Provider(providers, "openrouter").Id);
    }

    [Fact]
    public void Provider_WhenMissing_ThrowsNotFound()
    {
        Assert.Throws<CliNotFoundException>(() => RefResolver.Provider(Providers(), "nope"));
    }

    [Fact]
    public void Model_SplitsAtFirstSlash_ModelIdMayContainSlash()
    {
        var model = RefResolver.Model(Providers(), "openrouter/meta-llama/llama-3.1-8b:free");

        Assert.Equal(10, model.Id);
        Assert.Equal("openrouter", model.Provider?.Identifier);
    }

    [Fact]
    public void Model_ById_SetsProvider()
    {
        var model = RefResolver.Model(Providers(), "20");

        Assert.Equal("groq", model.Provider?.Identifier);
    }

    [Fact]
    public void Model_WithoutProviderPrefix_ThrowsUsage()
    {
        Assert.Throws<CliUsageException>(() => RefResolver.Model(Providers(), "llama-3.1-8b"));
    }

    [Fact]
    public void Account_ByProviderAndName()
    {
        var (provider, account) = RefResolver.Account(Providers(), "openrouter/default");

        Assert.Equal(1, provider.Id);
        Assert.Equal(50, account.Id);
    }

    [Fact]
    public void ClientKey_DuplicateNames_RequireId()
    {
        ClientKey[] keys = [new() { Id = 1, Name = "dev" }, new() { Id = 2, Name = "dev" }];

        Assert.Throws<CliNotFoundException>(() => RefResolver.ClientKey(keys, "dev"));
        Assert.Equal(2, RefResolver.ClientKey(keys, "2").Id);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public void TryParseId_RejectsNonPositive(string value)
    {
        Assert.False(RefResolver.TryParseId(value, out _));
    }
}
