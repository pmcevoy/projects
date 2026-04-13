using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
            var attackerUnits = _enricher.Enrich(AttackerText);
            var defenderUnits = _enricher.Enrich(DefenderText);

            var opts = new JsonSerializerOptions { WriteIndented = false };
            HttpContext.Session.SetString("attacker_army", JsonSerializer.Serialize(attackerUnits, opts));
            HttpContext.Session.SetString("defender_army",  JsonSerializer.Serialize(defenderUnits, opts));

            return RedirectToPage("/ArmyView");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to enrich army lists: {ex.Message}";
            return Page();
        }
    }
}
