using Wh40kArmyEnricher.Contracts;
using Wh40kArmyEnricher.Core;
using Wh40kArmyEnricher.Core.BsData;
using Wh40kArmyEnricher.Core.Parser;

namespace Wh40kArmyEnricher.Web.Services;

/// <summary>
/// Wraps the army list parser + enricher pipeline for use in the web layer.
/// The CatalogueStore is expected to be fully initialised before any calls to EnrichAsync.
/// </summary>
public class EnrichmentService
{
    private readonly ArmyListParser _parser;
    private readonly Enricher _enricher;

    public EnrichmentService(ArmyListParser parser, Enricher enricher)
    {
        _parser = parser;
        _enricher = enricher;
    }

    /// <summary>
    /// Parses and enriches an army list text export.
    /// Returns the enriched unit profiles, or throws on parse/enrichment failure.
    /// </summary>
    public IReadOnlyList<UnitProfile> Enrich(string armyListText)
    {
        var armyList = _parser.Parse(armyListText);
        var enriched = _enricher.Enrich(armyList);
        return enriched.Select(e => e.Profile).ToList();
    }
}
