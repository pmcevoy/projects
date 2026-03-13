using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using Wh40kArmyEnricher.Contracts;
using Wh40kArmyEnricher.Core;
using Wh40kArmyEnricher.Core.BsData;
using Wh40kArmyEnricher.Core.Parser;

namespace Wh40kArmyEnricher.Cli.Commands;

public static class EnrichCommand
{
    public static Command Build(IServiceProvider provider)
    {
        var armyListArg = new Argument<FileInfo>("army-list", "Path to the army list .txt export");
        var outputOpt = new Option<FileInfo?>("--output", "Output YAML file path (defaults to <army-list>.enriched.yaml)");
        var refreshOpt = new Option<bool>("--refresh-cache", "Force re-download of cached catalogue files");
        var dryRunOpt = new Option<bool>("--dry-run", "Parse and resolve without writing output");

        var cmd = new Command("enrich", "Enrich a single army list with full profile data")
        {
            armyListArg, outputOpt, refreshOpt, dryRunOpt
        };

        cmd.SetHandler(async (FileInfo armyListFile, FileInfo? output, bool refresh, bool dryRun) =>
        {
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("EnrichCommand");

            if (!armyListFile.Exists)
            {
                Console.Error.WriteLine($"File not found: {armyListFile.FullName}");
                return;
            }

            var text = await File.ReadAllTextAsync(armyListFile.FullName);
            var parser = new ArmyListParser();
            var army = parser.Parse(text);

            logger.LogInformation("Parsed army '{Name}' ({Points} pts, {Units} units)",
                army.Name, army.Points, army.Units.Count);

            var fetcher = provider.GetRequiredService<ICatalogueFetcher>();
            var catalogueParser = provider.GetRequiredService<CatalogueParser>();
            var nameResolver = provider.GetRequiredService<NameResolver>();
            var enricherLogger = provider.GetRequiredService<ILogger<Enricher>>();

            var store = new CatalogueStore(
                fetcher, catalogueParser,
                provider.GetRequiredService<ILogger<CatalogueStore>>(),
                forceRefresh: refresh);

            await store.InitialiseAsync();

            var enricher = new Enricher(store, nameResolver, enricherLogger);
            var enriched = enricher.Enrich(army);

            logger.LogInformation("Enriched {Count} units", enriched.Count);

            if (dryRun)
            {
                logger.LogInformation("Dry run complete — no output written");
                return;
            }

            var outPath = output?.FullName
                ?? Path.ChangeExtension(armyListFile.FullName, ".enriched.yaml");

            var profiles = enriched.Select(e => e.Attacker).ToList();
            var yaml = YamlSerialiser.Serialise(profiles);
            await File.WriteAllTextAsync(outPath, yaml);
            logger.LogInformation("Written to {Path}", outPath);

        }, armyListArg, outputOpt, refreshOpt, dryRunOpt);

        return cmd;
    }

}
