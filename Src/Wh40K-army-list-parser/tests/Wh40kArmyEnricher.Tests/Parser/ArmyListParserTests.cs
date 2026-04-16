using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Wh40kArmyEnricher.Core.Parser;

namespace Wh40kArmyEnricher.Tests.Parser;

public class ArmyListParserTests
{
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "black-templars-sample.txt");

    private static string FixtureText => File.ReadAllText(FixturePath);

    private readonly ArmyListParser _parser = new(NullLogger<ArmyListParser>.Instance);

    // ---------------------------------------------------------------------------
    // Army header
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_SetsArmyName()
    {
        var army = _parser.Parse(FixtureText);
        army.Name.Should().Be("Iron Canticle");
    }

    [Fact]
    public void Parse_SetsArmyPoints()
    {
        var army = _parser.Parse(FixtureText);
        army.Points.Should().Be(1970);
    }

    [Fact]
    public void Parse_SetsFaction()
    {
        var army = _parser.Parse(FixtureText);
        army.Faction.Should().Be("Black Templars");
    }

    [Fact]
    public void Parse_SetsDetachment()
    {
        var army = _parser.Parse(FixtureText);
        army.Detachment.Should().Be("Bastion Task Force");
    }

    // ---------------------------------------------------------------------------
    // Section categorisation
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_CategorisesCharacters()
    {
        var army = _parser.Parse(FixtureText);
        army.Units
            .Where(u => u.Category == "CHARACTERS")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_CategorisesBattleline()
    {
        var army = _parser.Parse(FixtureText);
        army.Units
            .Where(u => u.Category == "BATTLELINE")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_CategorisesDedicatedTransports()
    {
        var army = _parser.Parse(FixtureText);
        army.Units
            .Where(u => u.Category == "DEDICATED TRANSPORTS")
            .Should().ContainSingle(u => u.Name == "Impulsor");
    }

    [Fact]
    public void Parse_CategorisesOtherDatasheets()
    {
        var army = _parser.Parse(FixtureText);
        army.Units
            .Where(u => u.Category == "OTHER DATASHEETS")
            .Should().NotBeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Unit counts
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_HasCorrectTotalUnits()
    {
        var army = _parser.Parse(FixtureText);
        // 7 CHARACTERS + 3 BATTLELINE + 1 DEDICATED TRANSPORTS + 5 OTHER DATASHEETS = 16
        army.Units.Should().HaveCount(16);
    }

    // ---------------------------------------------------------------------------
    // Single-model unit (weapons-mode parsing)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Castellan_HasImplicitSingleModel()
    {
        var army = _parser.Parse(FixtureText);
        var castellan = army.Units.First(u => u.Name == "Castellan" && u.Points == 70);

        castellan.Models.Should().HaveCount(1);
        castellan.Models[0].Count.Should().Be(1);
        castellan.Models[0].Name.Should().Be("Castellan");
    }

    [Fact]
    public void Parse_Castellan_HasCorrectWeapons()
    {
        var army = _parser.Parse(FixtureText);
        var castellan = army.Units.First(u => u.Name == "Castellan" && u.Points == 70);
        var weapons = castellan.Models[0].Weapons;

        weapons.Should().Contain(w => w.Name == "Combi-weapon");
        weapons.Should().Contain(w => w.Name == "Master-crafted power weapon");
    }

    // ---------------------------------------------------------------------------
    // Multi-model unit (model-mode parsing)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_AssaultIntercessorSquad_HasTwoModelTypes()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.First(u => u.Name == "Assault Intercessor Squad");

        squad.Models.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_AssaultIntercessorSquad_ModelCounts()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.First(u => u.Name == "Assault Intercessor Squad");

        var sgt = squad.Models.Single(m => m.Name == "Assault Intercessor Sergeant");
        sgt.Count.Should().Be(1);

        var marines = squad.Models.Single(m => m.Name == "Assault Intercessor");
        marines.Count.Should().Be(4);
    }

    [Fact]
    public void Parse_AssaultIntercessor_HasChainswordAndBoltPistol()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.First(u => u.Name == "Assault Intercessor Squad");
        var marine = squad.Models.Single(m => m.Name == "Assault Intercessor");

        marine.Weapons.Should().Contain(w => w.Name == "Astartes chainsword");
        marine.Weapons.Should().Contain(w => w.Name == "Heavy bolt pistol");
    }

    // ---------------------------------------------------------------------------
    // Multi-model complex unit
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_CrusaderSquad_HasThreeModelTypes()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.Single(u => u.Name == "Crusader Squad");

        squad.Models.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_CrusaderSquad_InitiateCount()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.Single(u => u.Name == "Crusader Squad");
        var initiate = squad.Models.Single(m => m.Name == "Initiate");

        initiate.Count.Should().Be(11);
    }

    // ---------------------------------------------------------------------------
    // Enhancements
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_EmperorsChampion_HasBladesOfValourEnhancement()
    {
        var army = _parser.Parse(FixtureText);
        var champion = army.Units.Single(u => u.Name.Contains("Champion"));

        champion.Enhancements.Should().Contain("Blades of Valour");
    }

    [Fact]
    public void Parse_Marshal_HasHeroOfTheChapterEnhancement()
    {
        var army = _parser.Parse(FixtureText);
        var marshal = army.Units.Single(u => u.Name == "Marshal" && u.Points == 100);

        marshal.Enhancements.Should().Contain("Hero of the Chapter");
    }

    [Fact]
    public void Parse_UnitWithNoEnhancement_HasEmptyList()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.First(u => u.Name == "Assault Intercessor Squad");

        squad.Enhancements.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Warlord metadata is ignored (not treated as a model)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Helbrecht_WarlordNotTreatedAsModel()
    {
        var army = _parser.Parse(FixtureText);
        var helbrecht = army.Units.Single(u => u.Name == "High Marshal Helbrecht");

        helbrecht.Models.Should().ContainSingle();
        helbrecht.Models[0].Weapons.Should().NotContain(w => w.Name == "Warlord");
    }

    // ---------------------------------------------------------------------------
    // Total model counts (sum of ModelEntry.Count across all model types in a unit)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_AssaultIntercessorSquad_TotalModelCount()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.First(u => u.Name == "Assault Intercessor Squad");

        squad.Models.Sum(m => m.Count).Should().Be(5); // 1 Sgt + 4 Assault Intercessors
    }

    [Fact]
    public void Parse_CrusaderSquad_TotalModelCount()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.Single(u => u.Name == "Crusader Squad");

        squad.Models.Sum(m => m.Count).Should().Be(20); // 1 Sword Brother + 11 Initiates + 8 Neophytes
    }

    [Fact]
    public void Parse_ChaplainGrimaldus_TotalModelCount()
    {
        var army = _parser.Parse(FixtureText);
        var unit = army.Units.Single(u => u.Name == "Chaplain Grimaldus");

        unit.Models.Sum(m => m.Count).Should().Be(4); // 1 Grimaldus + 3 Cenobyte Servitors
    }

    // ---------------------------------------------------------------------------
    // Points
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_AllUnitsHaveNonZeroPoints()
    {
        var army = _parser.Parse(FixtureText);
        army.Units.Should().AllSatisfy(u => u.Points.Should().BePositive());
    }
}
