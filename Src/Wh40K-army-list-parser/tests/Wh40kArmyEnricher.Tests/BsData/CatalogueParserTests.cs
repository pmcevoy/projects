using FluentAssertions;
using Xunit;
using System.Xml.Linq;
using Wh40kArmyEnricher.Core.BsData;

namespace Wh40kArmyEnricher.Tests.BsData;

public class CatalogueParserTests
{
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "assault-intercessors-snippet.xml");

    private readonly CatalogueParser _parser = new();

    private async Task<Core.Models.CatalogueData> LoadFixtureAsync()
    {
        await using var stream = File.OpenRead(FixturePath);
        return await _parser.ParseAsync(stream, "assault-intercessors-snippet.xml");
    }

    // ---------------------------------------------------------------------------
    // Catalogue metadata
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Parse_SetsCatalogueId()
    {
        var cat = await LoadFixtureAsync();
        cat.Id.Should().Be("test-bt-001");
    }

    [Fact]
    public async Task Parse_SetsCatalogueName()
    {
        var cat = await LoadFixtureAsync();
        cat.Name.Should().Be("Test Black Templars");
    }

    // ---------------------------------------------------------------------------
    // Unit entries
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Parse_FindsAssaultIntercessorSquad()
    {
        var cat = await LoadFixtureAsync();
        cat.Entries.Should().Contain(e => e.Name == "Assault Intercessor Squad");
    }

    [Fact]
    public async Task Parse_AssaultIntercessorSquad_HasUnitStatline()
    {
        var cat = await LoadFixtureAsync();
        var entry = cat.Entries.Single(e => e.Name == "Assault Intercessor Squad");

        entry.Statline.Should().NotBeNull();
        entry.Statline!.Toughness.Should().Be(4);
        entry.Statline.Save.Should().Be(3);
        entry.Statline.Wounds.Should().Be(2);
    }

    // ---------------------------------------------------------------------------
    // Child model entries
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Parse_AssaultIntercessorSquad_HasChildModelEntries()
    {
        var cat = await LoadFixtureAsync();
        var squad = cat.Entries.Single(e => e.Name == "Assault Intercessor Squad");

        squad.ChildEntries.Should().Contain(c => c.Name == "Assault Intercessor Sergeant");
        squad.ChildEntries.Should().Contain(c => c.Name == "Assault Intercessor");
    }

    // ---------------------------------------------------------------------------
    // Weapon profiles
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Parse_AssaultIntercessor_HasChainsword()
    {
        var cat = await LoadFixtureAsync();
        var squad = cat.Entries.Single(e => e.Name == "Assault Intercessor Squad");
        var marine = squad.ChildEntries.Single(c => c.Name == "Assault Intercessor");

        marine.Weapons.Should().Contain(w => w.Name == "Astartes chainsword");
    }

    [Fact]
    public async Task Parse_Chainsword_HasCorrectStats()
    {
        var cat = await LoadFixtureAsync();
        var squad = cat.Entries.Single(e => e.Name == "Assault Intercessor Squad");
        var marine = squad.ChildEntries.Single(c => c.Name == "Assault Intercessor");
        var chainsword = marine.Weapons.Single(w => w.Name == "Astartes chainsword");

        chainsword.TypeName.Should().Be("Melee Weapons");
        chainsword.Range.Should().Be("Melee");
        chainsword.Attacks.Should().Be("4");
        chainsword.AP.Should().Be("-1");
        chainsword.Damage.Should().Be("1");
    }

    [Fact]
    public async Task Parse_HeavyBoltPistol_HasCorrectStats()
    {
        var cat = await LoadFixtureAsync();
        var squad = cat.Entries.Single(e => e.Name == "Assault Intercessor Squad");
        var marine = squad.ChildEntries.Single(c => c.Name == "Assault Intercessor");
        var hbp = marine.Weapons.Single(w => w.Name == "Heavy bolt pistol");

        hbp.TypeName.Should().Be("Ranged Weapons");
        hbp.Attacks.Should().Be("1");
        hbp.AP.Should().Be("-1");
    }

    [Fact]
    public async Task Parse_PlasmaPistol_HasPistolKeyword()
    {
        var cat = await LoadFixtureAsync();
        var squad = cat.Entries.Single(e => e.Name == "Assault Intercessor Squad");
        var sgt = squad.ChildEntries.Single(c => c.Name == "Assault Intercessor Sergeant");
        var plasma = sgt.Weapons.Single(w => w.Name == "Plasma pistol");

        plasma.Keywords.Should().Contain("Pistol");
    }

    // ---------------------------------------------------------------------------
    // Keywords (categoryLinks)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Parse_AssaultIntercessorSquad_HasInfantryKeyword()
    {
        var cat = await LoadFixtureAsync();
        var squad = cat.Entries.Single(e => e.Name == "Assault Intercessor Squad");

        squad.Keywords.Should().Contain("INFANTRY");
    }

    [Fact]
    public async Task Parse_AssaultIntercessorSquad_HasAdeptusAstartesKeyword()
    {
        var cat = await LoadFixtureAsync();
        var squad = cat.Entries.Single(e => e.Name == "Assault Intercessor Squad");

        squad.Keywords.Should().Contain("ADEPTUS ASTARTES");
    }

    // ---------------------------------------------------------------------------
    // Invuln save parsed from ability text
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Parse_Castellan_HasInvulnerableSaveFromAbility()
    {
        var cat = await LoadFixtureAsync();
        var castellan = cat.Entries.Single(e => e.Name == "Castellan");

        castellan.Statline.Should().NotBeNull();
        castellan.Statline!.InvulnerableSave.Should().Be(4);
    }

    // ---------------------------------------------------------------------------
    // Combi-weapon Rapid Fire keyword
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Parse_CombiWeapon_HasRapidFireKeyword()
    {
        var cat = await LoadFixtureAsync();
        var castellan = cat.Entries.Single(e => e.Name == "Castellan");
        var combi = castellan.Weapons.Single(w => w.Name == "Combi-weapon");

        combi.Keywords.Should().Contain("Rapid Fire 1");
    }
}
