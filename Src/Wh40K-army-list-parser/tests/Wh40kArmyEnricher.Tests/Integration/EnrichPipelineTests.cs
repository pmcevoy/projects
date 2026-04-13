using FluentAssertions;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wh40kArmyEnricher.Core;
using Wh40kArmyEnricher.Core.BsData;
using Wh40kArmyEnricher.Core.Models;
using Wh40kArmyEnricher.Core.Parser;

namespace Wh40kArmyEnricher.Tests.Integration;

/// <summary>
/// Runs the full enrichment pipeline against the Black Templars sample fixture
/// using saved BSData XML (no live network calls).
/// </summary>
public class EnrichPipelineTests
{
    private static string SampleArmyPath => Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "black-templars-sample.txt");

    private static string SnippetPath => Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "assault-intercessors-snippet.xml");

    private async Task<(IReadOnlyList<EnrichedUnit> Enriched, string ArmyName)> RunPipelineAsync()
    {
        var armyText = await File.ReadAllTextAsync(SampleArmyPath);
        var army = new ArmyListParser().Parse(armyText);

        var catalogueParser = new CatalogueParser();

        var fetcher = new Mock<ICatalogueFetcher>();
        fetcher
            .Setup(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => File.OpenRead(SnippetPath));
        fetcher
            .Setup(f => f.FetchRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[]");

        var store = new CatalogueStore(
            fetcher.Object, catalogueParser,
            NullLogger<CatalogueStore>.Instance);

        // Inject fixture catalogue directly (bypasses network)
        await using var fixtureStream = File.OpenRead(SnippetPath);
        var fixtureData = await catalogueParser.ParseAsync(fixtureStream, "assault-intercessors-snippet.xml");
        InjectCatalogue(store, fixtureData);

        var resolver = new NameResolver(NullLogger<NameResolver>.Instance);
        var enricher = new Enricher(store, resolver, NullLogger<Enricher>.Instance);

        return (enricher.Enrich(army), army.Name);
    }

    private static void InjectCatalogue(CatalogueStore store, CatalogueData data)
    {
        var loadedField = typeof(CatalogueStore).GetField("_loaded",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var loaded = (Dictionary<string, CatalogueData>)loadedField!.GetValue(store)!;
        loaded[data.Id] = data;

        var initField = typeof(CatalogueStore).GetField("_initialised",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        initField!.SetValue(store, true);
    }

    // ---------------------------------------------------------------------------
    // Basic pipeline smoke test
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pipeline_ProducesEnrichedUnitsForAllArmyUnits()
    {
        var (enriched, _) = await RunPipelineAsync();
        enriched.Should().NotBeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Assault Intercessors statline
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pipeline_AssaultIntercessors_HasToughness4()
    {
        var (enriched, _) = await RunPipelineAsync();
        var squad = enriched.FirstOrDefault(e =>
            e.Profile.Name.Contains("Assault Intercessor", StringComparison.OrdinalIgnoreCase));

        squad.Should().NotBeNull();
        squad!.Profile.Toughness.Should().Be(4);
    }

    [Fact]
    public async Task Pipeline_AssaultIntercessors_HasSave3()
    {
        var (enriched, _) = await RunPipelineAsync();
        var squad = enriched.FirstOrDefault(e =>
            e.Profile.Name.Contains("Assault Intercessor", StringComparison.OrdinalIgnoreCase));

        squad.Should().NotBeNull();
        squad!.Profile.Save.Should().Be(3);
    }

    [Fact]
    public async Task Pipeline_AssaultIntercessors_HasWounds2()
    {
        var (enriched, _) = await RunPipelineAsync();
        var squad = enriched.FirstOrDefault(e =>
            e.Profile.Name.Contains("Assault Intercessor", StringComparison.OrdinalIgnoreCase));

        squad.Should().NotBeNull();
        squad!.Profile.Wounds.Should().Be(2);
    }

    // ---------------------------------------------------------------------------
    // Weapon stats
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pipeline_Chainsword_HasApMinus1()
    {
        var (enriched, _) = await RunPipelineAsync();
        var squad = enriched.FirstOrDefault(e =>
            e.Profile.Name.Contains("Assault Intercessor", StringComparison.OrdinalIgnoreCase));

        squad.Should().NotBeNull();

        var chainsword = squad!.Profile.Models
            .SelectMany(m => m.Weapons)
            .FirstOrDefault(w => w.WeaponName.Contains("chainsword", StringComparison.OrdinalIgnoreCase));

        chainsword.Should().NotBeNull();
        chainsword!.Profiles.Should().ContainSingle();
        chainsword.Profiles[0].Ap.Should().Be(-1);
    }

    // ---------------------------------------------------------------------------
    // Attacker keywords
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Pipeline_AssaultIntercessors_AttackerHasInfantryKeyword()
    {
        var (enriched, _) = await RunPipelineAsync();
        var squad = enriched.FirstOrDefault(e =>
            e.Profile.Name.Contains("Assault Intercessor", StringComparison.OrdinalIgnoreCase));

        squad.Should().NotBeNull();
        squad!.Profile.Keywords.Should().Contain("INFANTRY");
    }

}
