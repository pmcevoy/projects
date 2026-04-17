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
    /// Returns the enriched unit profiles and the set of BSData catalogue IDs that were used.
    /// </summary>
    public (IReadOnlyList<UnitProfile> Profiles, IReadOnlySet<string> UsedCatalogueIds) Enrich(string armyListText)
    {
        var armyList = _parser.Parse(armyListText);
        var (units, usedIds) = _enricher.Enrich(armyList);
        return (units.Select(e => e.Profile).ToList(), usedIds);
    }
}
