using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Wh40kArmyEnricher.Web.Helpers;
using Wh40kArmyEnricher.Web.Services;

namespace Wh40kArmyEnricher.Web.Pages;

public class IndexModel : PageModel
{
    private readonly EnrichmentService _enricher;

    public IndexModel(EnrichmentService enricher)
    {
        _enricher = enricher;
    }

    public string? ErrorMessage { get; private set; }
    public string AttackerText { get; private set; } = "";
    public string DefenderText { get; private set; } = "";

    public void OnGet() { }

    public IActionResult OnPost(string attackerText, string defenderText)
    {
        AttackerText = attackerText ?? "";
        DefenderText = defenderText ?? "";

        if (string.IsNullOrWhiteSpace(AttackerText) || string.IsNullOrWhiteSpace(DefenderText))
        {
            ErrorMessage = "Both army lists are required.";
            return Page();
        }

        try
        {
            var (attackerUnits, attackerIds) = _enricher.Enrich(AttackerText);
            var (defenderUnits, defenderIds) = _enricher.Enrich(DefenderText);

            var usedIds = attackerIds.Union(defenderIds).ToList();

            HttpContext.Session.SetString("attacker_army", JsonSerializer.Serialize(attackerUnits, SessionJson.Options));
            HttpContext.Session.SetString("defender_army",  JsonSerializer.Serialize(defenderUnits, SessionJson.Options));
            HttpContext.Session.SetString("used_catalogue_ids", JsonSerializer.Serialize(usedIds));

            return RedirectToPage("/ArmyView");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to enrich army lists: {ex.Message}";
            return Page();
        }
    }
}
