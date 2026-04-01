using FluentAssertions;
using Xunit;
using Wh40kArmyEnricher.Core.Parser;

namespace Wh40kArmyEnricher.Tests.Parser;

/// <summary>
/// Parser tests for the Android Warhammer app export format (Death Guard sample).
///
/// Android format differences from iOS:
///   - Indentation-based hierarchy instead of • / ◦ bullet characters
///   - [2sp]•  = model (or single-model first weapon)
///   - [4sp]•  = first weapon of a model in a squad
///   - [4-6sp] = weapon continuation lines (no bullet)
///   - Enhancement: Name  (singular, on a • line) vs iOS  ◦ Enhancements: Name
///   - Metadata order: Faction / ForceSize / Detachment  (iOS: GameSystem / Faction / Detachment / ForceSize)
/// </summary>
public class ArmyListParserAndroidTests
{
    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "death-guard.txt");

    private static string FixtureText => File.ReadAllText(FixturePath);

    private readonly ArmyListParser _parser = new();

    // ---------------------------------------------------------------------------
    // Army header
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_SetsArmyName()
    {
        var army = _parser.Parse(FixtureText);
        army.Name.Should().Be("battle 3");
    }

    [Fact]
    public void Parse_Android_SetsArmyPoints()
    {
        var army = _parser.Parse(FixtureText);
        army.Points.Should().Be(2010);
    }

    [Fact]
    public void Parse_Android_SetsFaction()
    {
        var army = _parser.Parse(FixtureText);
        army.Faction.Should().Be("Death Guard");
    }

    [Fact]
    public void Parse_Android_SetsDetachment()
    {
        // Android puts the detachment line AFTER the force-size line
        var army = _parser.Parse(FixtureText);
        army.Detachment.Should().Be("Virulent Vectorium");
    }

    // ---------------------------------------------------------------------------
    // Section categorisation
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_CategorisesCharacters()
    {
        var army = _parser.Parse(FixtureText);
        army.Units.Where(u => u.Category == "CHARACTERS")
            .Should().HaveCount(4);
    }

    [Fact]
    public void Parse_Android_CategorisesBattleline()
    {
        var army = _parser.Parse(FixtureText);
        army.Units.Where(u => u.Category == "BATTLELINE")
            .Should().HaveCount(2);
    }

    [Fact]
    public void Parse_Android_CategorisesDedicatedTransport()
    {
        var army = _parser.Parse(FixtureText);
        army.Units.Where(u => u.Category == "DEDICATED TRANSPORTS")
            .Should().ContainSingle(u => u.Name == "Chaos Rhino");
    }

    [Fact]
    public void Parse_Android_HasCorrectTotalUnits()
    {
        var army = _parser.Parse(FixtureText);
        // 4 CHARACTERS + 2 BATTLELINE + 1 DEDICATED TRANSPORT + 8 OTHER DATASHEETS
        army.Units.Should().HaveCount(15);
    }

    // ---------------------------------------------------------------------------
    // Single-model unit — weapon-mode with continuation lines (no sub-bullet)
    // Biologus Putrifier: [2sp]• first weapon, [4sp] subsequent weapons
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_BiologusPutrifier_HasImplicitSingleModel()
    {
        var army = _parser.Parse(FixtureText);
        var unit = army.Units.Single(u => u.Name == "Biologus Putrifier");

        unit.Models.Should().HaveCount(1);
        unit.Models[0].Name.Should().Be("Biologus Putrifier");
        unit.Models[0].Count.Should().Be(1);
    }

    [Fact]
    public void Parse_Android_BiologusPutrifier_AllWeaponsParsed()
    {
        var army = _parser.Parse(FixtureText);
        var weapons = army.Units.Single(u => u.Name == "Biologus Putrifier").Models[0].Weapons;

        weapons.Should().Contain(w => w.Name == "Hyper blight grenades");
        weapons.Should().Contain(w => w.Name == "Injector pistol");
        weapons.Should().Contain(w => w.Name == "Plague knives");
    }

    // ---------------------------------------------------------------------------
    // Single-model unit — weapon-mode, all [2sp]• lines are weapons
    // Chaos Rhino: all weapons on [2sp]• / [4sp] continuation lines
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_ChaosRhino_HasImplicitSingleModel()
    {
        var army = _parser.Parse(FixtureText);
        var unit = army.Units.Single(u => u.Name == "Chaos Rhino");

        unit.Models.Should().HaveCount(1);
        unit.Models[0].Name.Should().Be("Chaos Rhino");
    }

    [Fact]
    public void Parse_Android_ChaosRhino_AllWeaponsParsed()
    {
        var army = _parser.Parse(FixtureText);
        var weapons = army.Units.Single(u => u.Name == "Chaos Rhino").Models[0].Weapons;

        weapons.Should().Contain(w => w.Name == "Armoured tracks");
        weapons.Should().Contain(w => w.Name == "Combi-bolter");
        weapons.Should().Contain(w => w.Name == "Havoc launcher");
    }

    // ---------------------------------------------------------------------------
    // Single-model unit — Warlord marker ignored, weapons on mixed-indent lines
    // Mortarion: • Warlord skipped; • 1x Lantern + [4sp] continuations are all weapons
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_Mortarion_WarlordNotTreatedAsWeapon()
    {
        var army = _parser.Parse(FixtureText);
        var mortarion = army.Units.Single(u => u.Name == "Mortarion");

        mortarion.Models.Should().HaveCount(1);
        mortarion.Models[0].Weapons.Should().NotContain(w => w.Name == "Warlord");
    }

    [Fact]
    public void Parse_Android_Mortarion_AllWeaponsParsed()
    {
        var army = _parser.Parse(FixtureText);
        var weapons = army.Units.Single(u => u.Name == "Mortarion").Models[0].Weapons;

        weapons.Should().Contain(w => w.Name == "Lantern");
        weapons.Should().Contain(w => w.Name == "Rotwind");
        weapons.Should().Contain(w => w.Name == "Silence");
    }

    // ---------------------------------------------------------------------------
    // Multi-model squad — model-mode with [4sp]• first weapon + [6sp] continuations
    // Plague Marines: Plague Champion (1x) + Plague Marine (4x)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_PlagueMarines_HasTwoModelTypes()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.First(u => u.Name == "Plague Marines");

        squad.Models.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_Android_PlagueMarines_ModelCounts()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.First(u => u.Name == "Plague Marines");

        squad.Models.Single(m => m.Name == "Plague Champion").Count.Should().Be(1);
        squad.Models.Single(m => m.Name == "Plague Marine").Count.Should().Be(4);
    }

    [Fact]
    public void Parse_Android_PlagueChampion_HasCorrectWeapons()
    {
        var army = _parser.Parse(FixtureText);
        var champion = army.Units.First(u => u.Name == "Plague Marines")
                           .Models.Single(m => m.Name == "Plague Champion");

        champion.Weapons.Should().Contain(w => w.Name == "Plasma gun");
        champion.Weapons.Should().Contain(w => w.Name == "Power fist");
    }

    [Fact]
    public void Parse_Android_PlagueMarineModel_HasCorrectWeapons()
    {
        var army = _parser.Parse(FixtureText);
        var marine = army.Units.First(u => u.Name == "Plague Marines")
                         .Models.Single(m => m.Name == "Plague Marine");

        marine.Weapons.Should().Contain(w => w.Name == "Blight launcher");
        marine.Weapons.Should().Contain(w => w.Name == "Heavy plague weapon");
        marine.Weapons.Should().Contain(w => w.Name == "Meltagun");
        marine.Weapons.Should().Contain(w => w.Name == "Plague knives");
        marine.Weapons.Should().Contain(w => w.Name == "Plague spewer");
    }

    // ---------------------------------------------------------------------------
    // Multi-model squad — simple, single weapon per model type
    // Poxwalkers: [2sp]• model, [4sp]• weapon
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_Poxwalkers_HasSingleModelType()
    {
        var army = _parser.Parse(FixtureText);
        var unit = army.Units.First(u => u.Name == "Poxwalkers");

        unit.Models.Should().HaveCount(1);
        unit.Models[0].Name.Should().Be("Poxwalker");
        unit.Models[0].Count.Should().Be(10);
    }

    [Fact]
    public void Parse_Android_Poxwalkers_HasCorrectWeapon()
    {
        var army = _parser.Parse(FixtureText);
        var unit = army.Units.First(u => u.Name == "Poxwalkers");

        unit.Models[0].Weapons.Should().ContainSingle(w => w.Name == "Improvised weapon");
    }

    // ---------------------------------------------------------------------------
    // Multi-model squad — model with [4sp]• first weapon + [6sp] continuations
    // Deathshroud Terminators: Champion (1x) + Terminator (2x)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_DeathshroudTerminators_HasTwoModelTypes()
    {
        var army = _parser.Parse(FixtureText);
        var unit = army.Units.First(u => u.Name == "Deathshroud Terminators");

        unit.Models.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_Android_DeathshroudChampion_HasCorrectWeapons()
    {
        var army = _parser.Parse(FixtureText);
        var champion = army.Units.First(u => u.Name == "Deathshroud Terminators")
                           .Models.Single(m => m.Name == "Deathshroud Champion");

        champion.Count.Should().Be(1);
        champion.Weapons.Should().Contain(w => w.Name == "Icon of Despair (Aura)");
        champion.Weapons.Should().Contain(w => w.Name == "Manreaper");
        champion.Weapons.Should().Contain(w => w.Name == "Plaguespurt gauntlet");
    }

    [Fact]
    public void Parse_Android_DeathshroudTerminator_HasCorrectWeapons()
    {
        var army = _parser.Parse(FixtureText);
        var terminator = army.Units.First(u => u.Name == "Deathshroud Terminators")
                             .Models.Single(m => m.Name == "Deathshroud Terminator");

        terminator.Count.Should().Be(2);
        terminator.Weapons.Should().Contain(w => w.Name == "Manreaper");
        terminator.Weapons.Should().Contain(w => w.Name == "Plaguespurt gauntlet");
    }

    // ---------------------------------------------------------------------------
    // Enhancements — Android format uses singular "Enhancement:" on a • line
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_LordOfContagion_HasEnhancement()
    {
        var army = _parser.Parse(FixtureText);
        var lord = army.Units.Single(u => u.Name == "Lord of Contagion" && u.Points == 145);

        lord.Enhancements.Should().Contain("Furnace of Plagues");
    }

    [Fact]
    public void Parse_Android_LordOfContagion_EnhancementNotTreatedAsWeapon()
    {
        var army = _parser.Parse(FixtureText);
        var lord = army.Units.Single(u => u.Name == "Lord of Contagion" && u.Points == 145);

        lord.Models[0].Weapons.Should().NotContain(w => w.Name.Contains("Enhancement"));
    }

    [Fact]
    public void Parse_Android_SecondLordOfContagion_HasDifferentEnhancement()
    {
        var army = _parser.Parse(FixtureText);
        var lord = army.Units.Single(u => u.Name == "Lord of Contagion" && u.Points == 130);

        lord.Enhancements.Should().Contain("Daemon Weapon of Nurgle");
    }

    // ---------------------------------------------------------------------------
    // Total model counts (sum of ModelEntry.Count across all model types in a unit)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_PlagueMarines_TotalModelCount()
    {
        var army = _parser.Parse(FixtureText);
        var squad = army.Units.First(u => u.Name == "Plague Marines");

        squad.Models.Sum(m => m.Count).Should().Be(5); // 1 Plague Champion + 4 Plague Marines
    }

    [Fact]
    public void Parse_Android_DeathshroudTerminators_TotalModelCount()
    {
        var army = _parser.Parse(FixtureText);
        var unit = army.Units.First(u => u.Name == "Deathshroud Terminators");

        unit.Models.Sum(m => m.Count).Should().Be(3); // 1 Champion + 2 Terminators
    }

    [Fact]
    public void Parse_Android_Poxwalkers_TotalModelCount()
    {
        var army = _parser.Parse(FixtureText);
        var unit = army.Units.First(u => u.Name == "Poxwalkers");

        unit.Models.Sum(m => m.Count).Should().Be(10);
    }

    [Fact]
    public void Parse_Android_Mortarion_TotalModelCount()
    {
        var army = _parser.Parse(FixtureText);
        var unit = army.Units.Single(u => u.Name == "Mortarion");

        unit.Models.Sum(m => m.Count).Should().Be(1);
    }

    // ---------------------------------------------------------------------------
    // Points
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_Android_AllUnitsHaveNonZeroPoints()
    {
        var army = _parser.Parse(FixtureText);
        army.Units.Should().AllSatisfy(u => u.Points.Should().BePositive());
    }
}
