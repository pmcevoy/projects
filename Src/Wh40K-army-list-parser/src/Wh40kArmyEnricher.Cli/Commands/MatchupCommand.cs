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

            var enricher = new Enricher(store, nameResolver, enricherLogger);
            var enrichedAttackers = enricher.Enrich(attackerArmy);
            var enrichedDefenders = enricher.Enrich(defenderArmy);

            // Build all AttachedUnit combinations for the attacker army
            var leaderResolver = new LeaderResolver();
            var allCombinations = leaderResolver.BuildAttachedUnits(
                enrichedAttackers.Select(e => e.Profile).ToList());

            // Filter attacker combinations by bodyguard name
            var filteredCombinations = attackerUnits.Length > 0
                ? allCombinations.Where(c => attackerUnits.Any(f =>
                    c.Bodyguard.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList()
                : allCombinations.ToList();

            // Resolve defender units — flag, interactive prompt, or all
            List<UnitProfile> filteredDefenders;
            if (defenderUnits.Length > 0)
            {
                filteredDefenders = enrichedDefenders
                    .Where(e => defenderUnits.Any(f =>
                        e.Profile.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
                    .Select(e => e.Profile)
                    .ToList();
            }
            else if (!Console.IsInputRedirected)
            {
                filteredDefenders = PromptDefenderSelection(enrichedDefenders.Select(e => e.Profile).ToList());
            }
            else
            {
                // Non-interactive context (piped/scripted): use all defenders
                filteredDefenders = enrichedDefenders.Select(e => e.Profile).ToList();
            }

            // Build all pairings (Cartesian product of combinations × defenders)
            var pairings = new List<Pairing>();
            foreach (var atk in filteredCombinations)
            {
                foreach (var def in filteredDefenders)
                {
                    pairings.Add(new Pairing
                    {
                        SimulationId = BuildSimulationId(
                            attackerArmy.Faction, atk,
                            defenderArmy.Faction, def),
                        Attacker = atk,
                        Defender = def
                    });
                }
            }

            logger.LogInformation("Generated {Count} pairings from {Combos} attacker combinations × {Defenders} defenders",
                pairings.Count, filteredCombinations.Count, filteredDefenders.Count);

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

    // ---------------------------------------------------------------------------
    // Interactive defender selection
    // ---------------------------------------------------------------------------

    private static List<UnitProfile> PromptDefenderSelection(List<UnitProfile> defenders)
    {
        Console.WriteLine();
        Console.WriteLine("Select defender unit(s):");
        for (int i = 0; i < defenders.Count; i++)
        {
            var d = defenders[i];
            Console.WriteLine($"  {i + 1,3}. {d.Name}  (#{d.ArmyListIndex}, {d.ModelCount} models)");
        }
        Console.Write("\nEnter unit numbers separated by commas (or press Enter for all): ");

        var input = Console.ReadLine() ?? "";
        if (string.IsNullOrWhiteSpace(input))
            return defenders;

        return input
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var n) && n >= 1 && n <= defenders.Count
                ? defenders[n - 1] : null)
            .OfType<UnitProfile>()
            .ToList();
    }

    // ---------------------------------------------------------------------------
    // SimulationId
    // ---------------------------------------------------------------------------

    private static string BuildSimulationId(
        string atkFaction, AttachedUnit attacker,
        string defFaction, UnitProfile defender)
    {
        var bodyguardSlug = Slug(attacker.Bodyguard.Name);
        string atkSlug;
        if (attacker.Leaders.Count == 0)
        {
            atkSlug = bodyguardSlug;
        }
        else
        {
            var leaderSlug = string.Join("_and_", attacker.Leaders.Select(l => Slug(l.Name)));
            atkSlug = $"{bodyguardSlug}_led_by_{leaderSlug}";
        }

        return $"{Abbrev(atkFaction)}_{atkSlug}_{attacker.Bodyguard.ArmyListIndex}" +
               $"_vs_{Abbrev(defFaction)}_{Slug(defender.Name)}_{defender.ArmyListIndex}";
    }

    private static string Abbrev(string faction) => string.Concat(
        faction.Split(' ', StringSplitOptions.RemoveEmptyEntries)
               .Select(w => w[0].ToString().ToLowerInvariant()));

    private static string Slug(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');

    private static string SanitiseName(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
}
