using FluentAssertions;
using ProbHammer.Core.Contracts;
using ProbHammer.Core.Parsing;

namespace ProbHammer.Tests.Parsing;

public class ArmyListParserTests
{
    private static readonly ArmyListParser Parser = new();

    private static string LoadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static readonly ArmyList Army =
        Parser.Parse(LoadFixture("black-templars-sample.txt"));

    [Fact]
    public void Parse_iOS_ArmyName() =>
        Army.Name.Should().Be("Iron Canticle");

    [Fact]
    public void Parse_iOS_ArmyPoints() =>
        Army.Points.Should().Be(2035);

    [Fact]
    public void Parse_iOS_Faction() =>
        Army.Faction.Should().Be("Black Templars");

    [Fact]
    public void Parse_iOS_Detachment() =>
        Army.Detachment.Should().Be("Bastion Task Force");

    [Fact]
    public void Parse_iOS_TotalUnitCount() =>
        Army.Units.Should().HaveCount(18);

    [Fact]
    public void Parse_iOS_CharacterCount() =>
        Army.Units.Where(u => u.Category == "CHARACTERS").Should().HaveCount(7);

    [Fact]
    public void Parse_iOS_BattlelineCount() =>
        Army.Units.Where(u => u.Category == "BATTLELINE").Should().HaveCount(5);

    [Fact]
    public void Parse_iOS_DedicatedTransportsCount() =>
        Army.Units.Where(u => u.Category == "DEDICATED TRANSPORTS").Should().HaveCount(1);

    [Fact]
    public void Parse_iOS_OtherDatasheetsCount() =>
        Army.Units.Where(u => u.Category == "OTHER DATASHEETS").Should().HaveCount(5);

    [Fact]
    public void Parse_iOS_AssaultIntercessors_TwoModels()
    {
        var unit = Army.Units.First(u => u.Name == "Assault Intercessor Squad");
        unit.Models.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_iOS_AssaultIntercessors_Sergeant()
    {
        var unit = Army.Units.First(u => u.Name == "Assault Intercessor Squad");
        var sgt = unit.Models.First(m => m.Name == "Assault Intercessor Sergeant");
        sgt.Count.Should().Be(1);
        sgt.Weapons.Should().Contain(w => w.Name == "Plasma pistol" && w.Count == 1);
        sgt.Weapons.Should().Contain(w => w.Name == "Power fist" && w.Count == 1);
    }

    [Fact]
    public void Parse_iOS_AssaultIntercessors_Troopers()
    {
        var unit = Army.Units.First(u => u.Name == "Assault Intercessor Squad");
        var troopers = unit.Models.First(m => m.Name == "Assault Intercessor");
        troopers.Count.Should().Be(4);
        troopers.Weapons.Should().Contain(w => w.Name == "Astartes chainsword" && w.Count == 4);
        troopers.Weapons.Should().Contain(w => w.Name == "Heavy bolt pistol" && w.Count == 4);
    }

    [Fact]
    public void Parse_iOS_ChaplainGrimaldus_TwoModelTypes()
    {
        var unit = Army.Units.First(u => u.Name == "Chaplain Grimaldus");
        unit.Models.Should().HaveCount(2);

        var grim = unit.Models.First(m => m.Name == "Chaplain Grimaldus");
        grim.Count.Should().Be(1);
        grim.Weapons.Should().Contain(w => w.Name == "Artificer crozius");
        grim.Weapons.Should().Contain(w => w.Name == "Plasma pistol");

        var servitors = unit.Models.First(m => m.Name == "Cenobyte Servitor");
        servitors.Count.Should().Be(3);
        servitors.Weapons.Should().Contain(w => w.Name == "Close combat weapon");
    }

    [Fact]
    public void Parse_iOS_CrusaderSquad_ThreeModels()
    {
        var unit = Army.Units.First(u => u.Name == "Crusader Squad");
        unit.Models.Should().HaveCount(3);
        unit.Models.Should().Contain(m => m.Name == "Sword Brother" && m.Count == 1);
        unit.Models.Should().Contain(m => m.Name == "Initiate" && m.Count == 5);
        unit.Models.Should().Contain(m => m.Name == "Neophyte" && m.Count == 4);
    }

    [Fact]
    public void Parse_iOS_Impulsor_SingleModelWithItems()
    {
        var unit = Army.Units.First(u => u.Name == "Impulsor");
        unit.Models.Should().HaveCount(1);
        unit.Models[0].Name.Should().Be("Impulsor");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Multi-melta");
        unit.Models[0].Weapons.Should().Contain(w => w.Name == "Shield Dome");
    }

    [Fact]
    public void Parse_iOS_SwordBrethren_OneModelType()
    {
        var unit = Army.Units.First(u => u.Name == "Sword Brethren Squad");
        unit.Models.Should().HaveCount(1);
        var brothers = unit.Models[0];
        brothers.Name.Should().Be("Sword Brother");
        brothers.Count.Should().Be(4);
        brothers.Weapons.Should().Contain(w => w.Name == "Master-crafted power weapon");
        brothers.Weapons.Should().Contain(w => w.Name == "Heavy bolt pistol");
    }

    [Fact]
    public void Parse_iOS_EmperorsChampion_SpecialApostrophe()
    {
        // Army export uses U+2019 RIGHT SINGLE QUOTATION MARK
        Army.Units.Should().Contain(u => u.Name.Contains("Champion"));
    }

    [Fact]
    public void Parse_iOS_TwoCastellans()
    {
        Army.Units.Where(u => u.Name == "Castellan").Should().HaveCount(2);
    }

    [Fact]
    public void Parse_iOS_Castellan_SingleModelWeapons()
    {
        var castellan = Army.Units.First(u => u.Name == "Castellan");
        castellan.Models.Should().HaveCount(1);
        castellan.Models[0].Weapons.Should().Contain(w => w.Name == "Combi-weapon");
        castellan.Models[0].Weapons.Should().Contain(w => w.Name == "Master-crafted power weapon");
    }
}
