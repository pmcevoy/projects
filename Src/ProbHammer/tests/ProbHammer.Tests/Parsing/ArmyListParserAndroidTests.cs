using FluentAssertions;
using ProbHammer.Core.Contracts;
using ProbHammer.Core.Parsing;

namespace ProbHammer.Tests.Parsing;

public class ArmyListParserAndroidTests
{
    private static readonly ArmyListParser Parser = new();

    private static string LoadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static readonly ArmyList Army =
        Parser.Parse(LoadFixture("death-guard.txt"));

    [Fact]
    public void Parse_Android_ArmyName() =>
        Army.Name.Should().Be("battle 3");

    [Fact]
    public void Parse_Android_ArmyPoints() =>
        Army.Points.Should().Be(2010);

    [Fact]
    public void Parse_Android_Faction() =>
        Army.Faction.Should().Be("Death Guard");

    [Fact]
    public void Parse_Android_Detachment() =>
        Army.Detachment.Should().Be("Virulent Vectorium");

    [Fact]
    public void Parse_Android_TotalUnitCount() =>
        Army.Units.Should().HaveCount(15);

    [Fact]
    public void Parse_Android_CharacterCount() =>
        Army.Units.Where(u => u.Category == "CHARACTERS").Should().HaveCount(4);

    [Fact]
    public void Parse_Android_BiolobusPutrifier_SingleModel()
    {
        var unit = Army.Units.First(u => u.Name == "Biologus Putrifier");
        unit.Models.Should().HaveCount(1);
        unit.Models[0].Name.Should().Be("Biologus Putrifier");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Hyper blight grenades");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Injector pistol");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Plague knives");
    }

    [Fact]
    public void Parse_Android_LordOfContagion_Enhancement()
    {
        var loc = Army.Units
            .Where(u => u.Name == "Lord of Contagion")
            .FirstOrDefault(u => u.Enhancements.Contains("Furnace of Plagues"));
        loc.Should().NotBeNull();
        loc!.Models.Should().HaveCount(1);
        loc.Models[0].Weapons.Should().Contain(w => w.Name == "Manreaper");
    }

    [Fact]
    public void Parse_Android_LordOfContagion_SecondEnhancement()
    {
        var loc = Army.Units
            .Where(u => u.Name == "Lord of Contagion")
            .FirstOrDefault(u => u.Enhancements.Contains("Daemon Weapon of Nurgle"));
        loc.Should().NotBeNull();
    }

    [Fact]
    public void Parse_Android_Mortarion_SingleModel()
    {
        var unit = Army.Units.First(u => u.Name == "Mortarion");
        unit.Models.Should().HaveCount(1);
        unit.Models[0].Name.Should().Be("Mortarion");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Lantern");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Rotwind");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Silence");
    }

    [Fact]
    public void Parse_Android_PlagueMarine_TwoModels()
    {
        var unit = Army.Units.First(u => u.Name == "Plague Marines");
        unit.Models.Should().HaveCount(2);

        var champion = unit.Models.First(m => m.Name == "Plague Champion");
        champion.Count.Should().Be(1);
        champion.Weapons.Should().Contain(w => w.Name == "Plasma gun");
        champion.Weapons.Should().Contain(w => w.Name == "Power fist");

        var marines = unit.Models.First(m => m.Name == "Plague Marine");
        marines.Count.Should().Be(4);
        marines.Weapons.Should().Contain(w => w.Name == "Blight launcher");
        marines.Weapons.Should().Contain(w => w.Name == "Meltagun");
        marines.Weapons.Should().Contain(w => w.Name == "Plague knives" && w.Count == 4);
    }

    [Fact]
    public void Parse_Android_DeathshroudTerminators_TwoModels()
    {
        var unit = Army.Units.First(u => u.Name == "Deathshroud Terminators");
        unit.Models.Should().HaveCount(2);

        var champion = unit.Models.First(m => m.Name == "Deathshroud Champion");
        champion.Count.Should().Be(1);
        champion.Weapons.Should().Contain(w => w.Name == "Icon of Despair (Aura)");
        champion.Weapons.Should().Contain(w => w.Name == "Manreaper");
        champion.Weapons.Should().Contain(w => w.Name == "Plaguespurt gauntlet" && w.Count == 2);

        var terminators = unit.Models.First(m => m.Name == "Deathshroud Terminator");
        terminators.Count.Should().Be(2);
        terminators.Weapons.Should().Contain(w => w.Name == "Manreaper" && w.Count == 2);
        terminators.Weapons.Should().Contain(w => w.Name == "Plaguespurt gauntlet" && w.Count == 2);
    }

    [Fact]
    public void Parse_Android_Poxwalkers_SquadModel()
    {
        var unit = Army.Units.First(u => u.Name == "Poxwalkers");
        unit.Models.Should().HaveCount(1);
        unit.Models[0].Name.Should().Be("Poxwalker");
        unit.Models[0].Count.Should().Be(10);
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Improvised weapon" && w.Count == 10);
    }

    [Fact]
    public void Parse_Android_ChaosRhino_SingleModel()
    {
        var unit = Army.Units.First(u => u.Name == "Chaos Rhino");
        unit.Models.Should().HaveCount(1);
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Armoured tracks");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Combi-bolter");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Havoc launcher");
    }

    [Fact]
    public void Parse_Android_FoetidBloatDrone_ThreeUnits()
    {
        Army.Units.Where(u => u.Name.StartsWith("Foetid Bloat-Drone")).Should().HaveCount(3);
    }

    [Fact]
    public void Parse_Android_FoetidBloatDrone_Weapons()
    {
        var unit = Army.Units.First(u => u.Name.StartsWith("Foetid Bloat-Drone"));
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Heavy blight launcher");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Plague probe");
    }

    [Fact]
    public void Parse_Android_PlagueburtCrawler_SingleModel()
    {
        var unit = Army.Units.First(u => u.Name == "Plagueburst Crawler");
        unit.Models.Should().HaveCount(1);
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Plagueburst mortar");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Entropy cannon" && w.Count == 2);
    }

    [Fact]
    public void Parse_Android_TwoPlagueMarineSquads()
    {
        Army.Units.Where(u => u.Name == "Plague Marines").Should().HaveCount(2);
    }

    [Fact]
    public void Parse_Android_TwoDeathshroudUnits()
    {
        Army.Units.Where(u => u.Name == "Deathshroud Terminators").Should().HaveCount(2);
    }
}
