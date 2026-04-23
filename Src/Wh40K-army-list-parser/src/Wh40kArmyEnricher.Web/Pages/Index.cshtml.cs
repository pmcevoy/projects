using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Wh40kArmyEnricher.Core.Catalogue;
using Wh40kArmyEnricher.Core.Enrichment;
using Wh40kArmyEnricher.Core.Parsing;
using Wh40kArmyEnricher.Web.Helpers;

namespace Wh40kArmyEnricher.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ArmyListParser _parser;
    private readonly Enricher _enricher;
    private readonly CatalogueStore _store;

    public IndexModel(ArmyListParser parser, Enricher enricher, CatalogueStore store)
    {
        _parser = parser;
        _enricher = enricher;
        _store = store;
    }

    [BindProperty]
    public string AttackerList { get; set; } = string.Empty;

    [BindProperty]
    public string DefenderList { get; set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public void OnGet() { }

    public IActionResult OnPost()
    {
        if (string.IsNullOrWhiteSpace(AttackerList) || string.IsNullOrWhiteSpace(DefenderList))
        {
            ErrorMessage = "Both army lists are required.";
            return Page();
        }

        if (!_store.IsInitialised)
        {
            ErrorMessage = "Catalogues are still loading — please try again in a moment.";
            return Page();
        }

        try
        {
            var attackerArmy = _parser.Parse(AttackerList);
            var defenderArmy = _parser.Parse(DefenderList);

            var (attackerUnits, attackerIds) = _enricher.Enrich(attackerArmy);
            var (defenderUnits, defenderIds) = _enricher.Enrich(defenderArmy);

            var usedIds = attackerIds.Union(defenderIds).ToList();

            HttpContext.Session.SetString("attacker_army",
                JsonSerializer.Serialize(attackerUnits, SessionJson.Options));
            HttpContext.Session.SetString("defender_army",
                JsonSerializer.Serialize(defenderUnits, SessionJson.Options));
            HttpContext.Session.SetString("used_catalogue_ids",
                JsonSerializer.Serialize(usedIds));

            return RedirectToPage("/ArmyView");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error processing army lists: {ex.Message}";
            return Page();
        }
    }
}
