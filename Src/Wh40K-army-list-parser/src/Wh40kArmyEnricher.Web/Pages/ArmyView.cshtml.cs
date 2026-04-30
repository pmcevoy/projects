using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Wh40kArmyEnricher.Core.Catalogue;
using Wh40kArmyEnricher.Core.Contracts;
using Wh40kArmyEnricher.Web.Helpers;

namespace Wh40kArmyEnricher.Web.Pages;

public class ArmyViewModel : PageModel
{
    private readonly CatalogueStore _store;

    public ArmyViewModel(CatalogueStore store) => _store = store;

    public List<UnitProfile> Attackers { get; private set; } = [];
    public List<UnitProfile> Defenders { get; private set; } = [];
    public List<CatalogueVersionInfo> CatalogueVersions { get; private set; } = [];

    public IActionResult OnGet()
    {
        var attackerJson = HttpContext.Session.GetString("attacker_army");
        var defenderJson = HttpContext.Session.GetString("defender_army");

        if (attackerJson is null || defenderJson is null)
            return RedirectToPage("/Index");

        Attackers = JsonSerializer.Deserialize<List<UnitProfile>>(attackerJson, SessionJson.Options) ?? [];
        Defenders = JsonSerializer.Deserialize<List<UnitProfile>>(defenderJson, SessionJson.Options) ?? [];

        var usedIdsJson = HttpContext.Session.GetString("used_catalogue_ids");
        var usedIds = usedIdsJson is not null
            ? JsonSerializer.Deserialize<List<string>>(usedIdsJson) ?? []
            : [];

        CatalogueVersions = usedIds
            .Select(id => _store.GetCatalogue(id))
            .OfType<CatalogueData>()
            .Select(c => new CatalogueVersionInfo(c.Name, c.Revision))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Page();
    }
}

public record CatalogueVersionInfo(string Name, int Revision);
