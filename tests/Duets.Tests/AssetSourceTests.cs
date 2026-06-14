namespace Duets.Tests;

public sealed class AssetSourceTests
{
    // From(text factory) round-trip

    [Fact]
    public async Task GetStringAsync_over_From_returns_original_string()
    {
        const string expected = "hello, world!";
        var source = AssetSources.From(_ => Task.FromResult(expected));

        var actual = await source.GetStringAsync();

        Assert.Equal(expected, actual);
    }

    // FromBytes binary round-trip

    [Fact]
    public async Task FromBytes_GetBytesAsync_returns_exact_bytes()
    {
        var expected = new byte[] { 0x00, 0xFF, 0x10 };
        var source = AssetSources.FromBytes(_ => Task.FromResult(expected));

        var actual = await source.GetBytesAsync();

        Assert.Equal(expected, actual);
    }

    // WithDiskCache byte caching

    [Fact]
    public async Task WithDiskCache_returns_identical_bytes_on_second_read()
    {
        var expected = new byte[] { 0x01, 0x02, 0x03, 0xFE, 0xFF };
        var cacheFile = Path.Combine(
            Path.GetTempPath(),
            $"duets-test-diskCache-{Guid.NewGuid():N}.bin"
        );
        try
        {
            var source = AssetSources
                .FromBytes(_ => Task.FromResult(expected))
                .WithDiskCache(cacheFile, TimeSpan.FromMinutes(10));

            // First call — populates cache.
            var first = await source.GetBytesAsync();
            // Second call — reads from cache.
            var second = await source.GetBytesAsync();

            Assert.Equal(expected, first);
            Assert.Equal(expected, second);
        }
        finally
        {
            if (File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
            }
        }
    }
}
