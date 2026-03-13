using FluentAssertions;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Xml.Linq;
using Wh40kArmyEnricher.Core.BsData;
using Wh40kArmyEnricher.Core.Models;

namespace Wh40kArmyEnricher.Tests.BsData;

public class NameResolverTests
{
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "assault-intercessors-snippet.xml");

    private readonly CatalogueParser _catalogueParser = new();
    private readonly NameResolver _resolver = new(NullLogger<NameResolver>.Instance);

    private async Task<CatalogueStore> BuildStoreAsync()
    {
        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher
            .Setup(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => File.OpenRead(FixturePath));

        // Simulate pre-initialised store by loading the fixture directly
        var store = new TestCatalogueStore(fetcher.Object, _catalogueParser,
            NullLogger<CatalogueStore>.Instance);
        await store.LoadFixtureAsync(FixturePath, _catalogueParser);
        return store;
    }

    // ---------------------------------------------------------------------------
    // Exact match
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResolveUnit_ExactMatch_ReturnsEntry()
    {
        var store = await BuildStoreAsync();
        var result = _resolver.ResolveUnit("Assault Intercessor Squad", store);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Assault Intercessor Squad");
    }

    [Fact]
    public async Task ResolveUnit_CaseInsensitiveMatch_ReturnsEntry()
    {
        var store = await BuildStoreAsync();
        var result = _resolver.ResolveUnit("assault intercessor squad", store);

        result.Should().NotBeNull();
    }

    // ---------------------------------------------------------------------------
    // Count-stripped match
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResolveUnit_CountStrippedMatch_ReturnsEntry()
    {
        var store = await BuildStoreAsync();
        var result = _resolver.ResolveUnit("1x Assault Intercessor Squad", store);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Assault Intercessor Squad");
    }

    // ---------------------------------------------------------------------------
    // Fuzzy match
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResolveUnit_FuzzyMatch_AboveThreshold_ReturnsEntry()
    {
        var store = await BuildStoreAsync();
        // Slight typo / pluralisation difference
        var result = _resolver.ResolveUnit("Assault Intercessors Squad", store);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveUnit_FuzzyMatch_BelowThreshold_ReturnsNull()
    {
        var store = await BuildStoreAsync();
        var result = _resolver.ResolveUnit("Death Guard Plague Marines", store);

        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Not found
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResolveUnit_CompletelyUnknown_ReturnsNull()
    {
        var store = await BuildStoreAsync();
        var result = _resolver.ResolveUnit("Xxxxxxxxxxx Unit That Does Not Exist", store);

        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Weapon resolution
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResolveWeapon_ExactMatch_InUnitScope_ReturnsData()
    {
        var store = await BuildStoreAsync();
        var unitEntry = _resolver.ResolveUnit("Assault Intercessor Squad", store)!;
        var marineEntry = unitEntry.ChildEntries.Single(c => c.Name == "Assault Intercessor");

        var result = _resolver.ResolveWeapon("Astartes chainsword", unitEntry, marineEntry, store);

        result.Should().NotBeNull();
        result!.AP.Should().Be("-1");
    }
}

/// <summary>
/// Test helper: a CatalogueStore whose catalogue data is loaded directly from a fixture file
/// without requiring a real ICatalogueFetcher.
/// </summary>
internal class TestCatalogueStore : CatalogueStore
{
    public TestCatalogueStore(ICatalogueFetcher fetcher, CatalogueParser parser,
        Microsoft.Extensions.Logging.ILogger<CatalogueStore> logger)
        : base(fetcher, parser, logger) { }

    public async Task LoadFixtureAsync(string fixturePath, CatalogueParser parser)
    {
        await using var stream = File.OpenRead(fixturePath);
        var data = await parser.ParseAsync(stream, Path.GetFileName(fixturePath));
        InjectCatalogue(data);
    }

    // Expose internal state for testing
    public void InjectCatalogue(CatalogueData data)
    {
        // Access the protected dictionary via reflection for test purposes
        var field = typeof(CatalogueStore).GetField("_loaded",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dict = (Dictionary<string, CatalogueData>)field!.GetValue(this)!;
        dict[data.Id] = data;

        var initialised = typeof(CatalogueStore).GetField("_initialised",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        initialised!.SetValue(this, true);
    }
}
