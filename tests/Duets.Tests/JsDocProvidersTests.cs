using System.Reflection;

namespace Duets.Tests;

public sealed class JsDocProvidersTests
{
    private sealed class StubProvider(string? text) : IJsDocProvider
    {
        public string? Get(MemberInfo member)
        {
            return text;
        }
    }

    private sealed class ThrowingProvider : IJsDocProvider
    {
        public string? Get(MemberInfo member)
        {
            throw new InvalidOperationException("provider error");
        }
    }

    [Fact]
    public void Add_provider_fires_ProviderAdded()
    {
        var providers = new JsDocProviders();
        var fired = false;
        providers.ProviderAdded += () => fired = true;

        providers.Add(new StubProvider("text"));

        Assert.True(fired);
    }

    [Fact]
    public void Add_xml_fires_ProviderAdded()
    {
        var providers = new JsDocProviders();
        var fired = false;
        providers.ProviderAdded += () => fired = true;

        providers.Add("<doc><assembly><name>T</name></assembly><members></members></doc>");

        Assert.True(fired);
    }

    [Fact]
    public void Get_returns_first_non_null_result()
    {
        var providers = new JsDocProviders();
        providers.Add(new StubProvider(null));
        providers.Add(new StubProvider("from second"));
        providers.Add(new StubProvider("from third"));

        Assert.Equal("from second", providers.Get(typeof(string)));
    }

    [Fact]
    public void Get_returns_null_when_all_providers_return_null()
    {
        var providers = new JsDocProviders();
        providers.Add(new StubProvider(null));
        providers.Add(new StubProvider(null));

        Assert.Null(providers.Get(typeof(string)));
    }

    [Fact]
    public void Get_returns_null_when_no_providers_registered()
    {
        var providers = new JsDocProviders();

        Assert.Null(providers.Get(typeof(string)));
    }

    [Fact]
    public void Get_returns_null_when_sole_provider_throws()
    {
        var providers = new JsDocProviders();
        providers.Add(new ThrowingProvider());

        Assert.Null(providers.Get(typeof(string)));
    }

    [Fact]
    public void Get_skips_throwing_provider_and_falls_through_to_next()
    {
        var providers = new JsDocProviders();
        providers.Add(new ThrowingProvider());
        providers.Add(new StubProvider("from second"));

        Assert.Equal("from second", providers.Get(typeof(string)));
    }
}
