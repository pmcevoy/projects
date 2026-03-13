using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Text.RegularExpressions;
using Wh40kArmyEnricher.Contracts;
using Wh40kArmyEnricher.Core;
using Wh40kArmyEnricher.Core.BsData;
using Wh40kArmyEnricher.Core.Parser;

namespace Wh40kArmyEnricher.Cli.Commands;

public static class MatchupCommand
{
    public static Command Build(IServiceProvider provider)
    {
        var attackerArg = new Argument<FileInfo>("attacker", "Attacker army list .txt export");
        var defenderArg = new Argument<FileInfo>("defender", "Defender army list .txt export");
        var outputOpt = new Option<FileInfo?>("--output", "Output YAML file path");
        var refreshOpt = new Option<bool>("--refresh-cache", "Force re-download of cached catalogue files");
        var attackerUnitOpt = new Option<string[]>("--attacker-unit", "Filter attacker units by name (repeatable)")
            { AllowMultipleArgumentsPerToken = false, Arity = ArgumentArity.ZeroOrMore };
        var defenderUnitOpt = new Option<string[]>("--defender-unit", "Filter defender units by name (repeatable)")
            { AllowMultipleArgumentsPerToken = false, Arity = ArgumentArity.ZeroOrMore };

        var cmd = new Command("matchup", "Generate all attacker/defender pairings from two army lists")
        {
            attackerArg, defenderArg, outputOpt, refreshOpt, attackerUnitOpt, defenderUnitOpt
        };

        cmd.SetHandler(async (FileInfo attackerFile, FileInfo defenderFile, FileInfo? output,
            bool refresh, string[] attackerUnits, string[] defenderUnits) =>
        {
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("MatchupCommand");

            var armyParser = new ArmyListParser();
            var attackerArmy = armyParser.Parse(await File.ReadAllTextAsync(attackerFile.FullName));
            var defenderArmy = armyParser.Parse(await File.ReadAllTextAsync(defenderFile.FullName));

            logger.LogInformation("Attacker: '{Name}' ({Points} pts)", attackerArmy.Name, attackerArmy.Points);
            logger.LogInformation("Defender: '{Name}' ({Points} pts)", defenderArmy.Name, defenderArmy.Points);

            var fetcher = provider.GetRequiredService<ICatalogueFetcher>();
            var catalogueParser = provider.GetRequiredService<CatalogueParser>();
            var nameResolver = provider.GetRequiredService<NameResolver>();
            var storeLogger = provider.GetRequiredService<ILogger<CatalogueStore>>();
            var enricherLogger = provider.GetRequiredService<ILogger<Enricher>>();

            var store = new CatalogueStore(fetcher, catalogueParser, storeLogger, forceRefresh: refresh);
            await store.InitialiseAsync();

            // Load catalogues for both armies
            await store.LoadCatalogueAsync(GuessCatalogueFilename(attackerArmy.Faction, attackerArmy.GameSystem));
            await store.LoadCatalogueAsync(GuessCatalogueFilename(defenderArmy.Faction, defenderArmy.GameSystem));

            var enricher = new Enricher(store, nameResolver, enricherLogger);
            var enrichedAttackers = enricher.Enrich(attackerArmy);
            var enrichedDefenders = enricher.Enrich(defenderArmy);

            // Apply unit filters
            var filteredAttackers = attackerUnits.Length > 0
                ? enrichedAttackers.Where(e => attackerUnits.Any(f =>
                    e.Attacker.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList()
                : enrichedAttackers.ToList();

            var filteredDefenders = defenderUnits.Length > 0
                ? enrichedDefenders.Where(e => defenderUnits.Any(f =>
                    e.Defender.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList()
                : enrichedDefenders.ToList();

            // Build all pairings
            var pairings = new List<Pairing>();
            foreach (var atk in filteredAttackers)
            {
                foreach (var def in filteredDefenders)
                {
                    pairings.Add(new Pairing
                    {
                        SimulationId = BuildSimulationId(
                            attackerArmy.Faction, atk.Attacker.Name,
                            defenderArmy.Faction, def.Defender.Name),
                        Attacker = atk.Attacker,
                        Defender = def.Defender
                    });
                }
            }

            logger.LogInformation("Generated {Count} pairings", pairings.Count);

            var pairingFile = new PairingFile
            {
                AttackerArmy = attackerArmy.Name,
                DefenderArmy = defenderArmy.Name,
                GeneratedUtc = DateTime.UtcNow.ToString("o"),
                SimulationDefaults = new SimulationDefaults { WithinHalfRange = false, Runs = 10000 },
                Pairings = pairings
            };

            var outPath = output?.FullName
                ?? $"matchup_{SanitiseName(attackerArmy.Name)}_vs_{SanitiseName(defenderArmy.Name)}.yaml";

            await File.WriteAllTextAsync(outPath, YamlSerialiser.Serialise(pairingFile));
            logger.LogInformation("Written to {Path}", outPath);

        }, attackerArg, defenderArg, outputOpt, refreshOpt, attackerUnitOpt, defenderUnitOpt);

        return cmd;
    }

    private static string BuildSimulationId(string atkFaction, string atkUnit, string defFaction, string defUnit)
    {
        static string Abbrev(string faction) => string.Concat(
            faction.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .Select(w => w[0].ToString().ToLowerInvariant()));

        static string Slug(string name) =>
            Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');

        return $"{Abbrev(atkFaction)}_{Slug(atkUnit)}_vs_{Abbrev(defFaction)}_{Slug(defUnit)}";
    }

    private static string SanitiseName(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');

    private static string GuessCatalogueFilename(string faction, string gameSystem) =>
        faction.Trim() switch
        {
            "Black Templars" => "Imperium - Black Templars.cat",
            "Space Marines" => "Imperium - Space Marines.cat",
            "Death Guard" => "Chaos - Death Guard.cat",
            "Chaos Space Marines" => "Chaos - Chaos Space Marines.cat",
            "Tyranids" => "Tyranids - Tyranids.cat",
            "Necrons" => "Necrons - Necrons.cat",
            "Orks" => "Orks - Orks.cat",
            "Tau Empire" or "T'au Empire" => "T'au Empire - T'au Empire.cat",
            "Aeldari" => "Aeldari - Aeldari.cat",
            "Drukhari" => "Aeldari - Drukhari.cat",
            _ => $"{gameSystem} - {faction}.cat"
        };
}
