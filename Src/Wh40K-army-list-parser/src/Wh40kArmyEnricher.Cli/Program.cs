using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using Wh40kArmyEnricher.Cli.Commands;
using Wh40kArmyEnricher.Core;
using Wh40kArmyEnricher.Core.BsData;

var services = new ServiceCollection();

services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

services.AddHttpClient("bsdata", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "wh40k-army-enricher/1.0");
});

services.AddSingleton<CatalogueParser>();
services.AddSingleton<ICatalogueFetcher, CatalogueFetcher>();
services.AddSingleton<NameResolver>();
services.AddSingleton<Enricher>();

// CatalogueStore is created with forceRefresh from CLI flag — resolved per-command
var provider = services.BuildServiceProvider();

var rootCommand = new RootCommand("Warhammer 40K army list enricher");

rootCommand.AddCommand(EnrichCommand.Build(provider));
rootCommand.AddCommand(MatchupCommand.Build(provider));

return await rootCommand.InvokeAsync(args);
