using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Wh40kSim.Models;

namespace Wh40kSim;

public static class YamlLoader
{
    public static SimulationConfig Load(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var raw = deserializer.Deserialize<RawConfig>(yaml);

        return Map(raw);
    }

    private static SimulationConfig Map(RawConfig raw)
    {
        var rw = raw.Weapon;
        var ra = raw.Attacker;
        var rd = raw.Defender;

        var weapon = new WeaponProfile
        {
            Name = rw.Name,
            Attacks = ParseDice(rw.Attacks),
            Skill = rw.Skill,
            Strength = rw.Strength,
            Ap = rw.Ap,
            Damage = ParseDice(rw.Damage),
            Abilities = new WeaponAbilities
            {
                Torrent = rw.Abilities.Torrent,
                Blast = rw.Abilities.Blast,
                Melta = rw.Abilities.Melta,
                RapidFire = rw.Abilities.RapidFire,
                SustainedHits = rw.Abilities.SustainedHits,
                LethalHits = rw.Abilities.LethalHits,
                DevastatingWounds = rw.Abilities.DevastatingWounds,
                Anti = rw.Abilities.Anti,
            },
            WithinHalfRange = rw.WithinHalfRange,
        };

        var attacker = new AttackerProfile
        {
            Name = ra.Name,
            Models = ra.Models,
            Weapon = weapon,
            Rerolls = new RerollOptions
            {
                HitRerollOnes = ra.Rerolls.HitRerollOnes,
                HitRerollAll = ra.Rerolls.HitRerollAll,
                WoundRerollOnes = ra.Rerolls.WoundRerollOnes,
                WoundRerollAll = ra.Rerolls.WoundRerollAll,
            },
            CriticalHitsOn = ra.CriticalHitsOn,
        };

        var defender = new DefenderProfile
        {
            Name = rd.Name,
            Models = rd.Models,
            Toughness = rd.Toughness,
            Save = rd.Save,
            InvulnerableSave = rd.InvulnerableSave,
            Wounds = rd.Wounds,
            FeelNoPain = rd.FeelNoPain,
            Keywords = rd.Keywords,
        };

        return new SimulationConfig
        {
            SimulationRuns = raw.SimulationRuns,
            Attacker = attacker,
            Defender = defender,
        };
    }

    private static DiceExpression ParseDice(object? value) =>
        value switch
        {
            int i => DiceExpression.Fixed(i),
            string s => DiceExpression.Parse(s),
            _ => DiceExpression.Fixed(1),
        };

    // --------------- Raw POCO classes (mutable, for YamlDotNet) ---------------

    private class RawConfig
    {
        public int SimulationRuns { get; set; } = 100_000;
        public RawAttacker Attacker { get; set; } = new();
        public RawDefender Defender { get; set; } = new();

        // Convenience accessor
        public RawWeapon Weapon => Attacker.Weapon;
    }

    private class RawAttacker
    {
        public string Name { get; set; } = string.Empty;
        public int Models { get; set; }
        public RawWeapon Weapon { get; set; } = new();
        public RawRerolls Rerolls { get; set; } = new();
        public int CriticalHitsOn { get; set; } = 6;
    }

    private class RawWeapon
    {
        public string Name { get; set; } = string.Empty;
        public object Attacks { get; set; } = 1;
        public int Skill { get; set; }
        public int Strength { get; set; }
        public int Ap { get; set; }
        public object Damage { get; set; } = 1;
        public RawAbilities Abilities { get; set; } = new();
        public bool WithinHalfRange { get; set; }
    }

    private class RawAbilities
    {
        public bool Torrent { get; set; }
        public bool Blast { get; set; }
        public int Melta { get; set; }
        public int RapidFire { get; set; }
        public int SustainedHits { get; set; }
        public bool LethalHits { get; set; }
        public bool DevastatingWounds { get; set; }
        public Dictionary<string, int> Anti { get; set; } = new();
    }

    private class RawRerolls
    {
        public bool HitRerollOnes { get; set; }
        public bool HitRerollAll { get; set; }
        public bool WoundRerollOnes { get; set; }
        public bool WoundRerollAll { get; set; }
    }

    private class RawDefender
    {
        public string Name { get; set; } = string.Empty;
        public int Models { get; set; }
        public int Toughness { get; set; }
        public int Save { get; set; }
        public int? InvulnerableSave { get; set; }
        public int Wounds { get; set; }
        public int? FeelNoPain { get; set; }
        public List<string> Keywords { get; set; } = new();
    }
}
