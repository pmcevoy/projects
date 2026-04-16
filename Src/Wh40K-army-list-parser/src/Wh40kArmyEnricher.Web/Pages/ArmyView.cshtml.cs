using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Wh40kArmyEnricher.Contracts;
using Wh40kArmyEnricher.Web.Helpers;

namespace Wh40kArmyEnricher.Web.Pages;

public class ArmyViewModel : PageModel
{
    public IReadOnlyList<UnitProfile> AttackerArmy { get; private set; } = [];
    public IReadOnlyList<UnitProfile> DefenderArmy { get; private set; } = [];

    public IActionResult OnGet()
    {
        var attackerJson = HttpContext.Session.GetString("attacker_army");
        var defenderJson = HttpContext.Session.GetString("defender_army");

        if (attackerJson == null || defenderJson == null)
            return RedirectToPage("/Index");

        AttackerArmy = JsonSerializer.Deserialize<List<UnitProfile>>(attackerJson, SessionJson.Options) ?? [];
        DefenderArmy = JsonSerializer.Deserialize<List<UnitProfile>>(defenderJson, SessionJson.Options) ?? [];

        return Page();
    }

    public IActionResult OnPostSwap()
    {
        var attackerJson = HttpContext.Session.GetString("attacker_army");
        var defenderJson = HttpContext.Session.GetString("defender_army");

        if (attackerJson == null || defenderJson == null)
            return RedirectToPage("/Index");

        HttpContext.Session.SetString("attacker_army", defenderJson);
        HttpContext.Session.SetString("defender_army", attackerJson);

        return RedirectToPage();
    }
}
