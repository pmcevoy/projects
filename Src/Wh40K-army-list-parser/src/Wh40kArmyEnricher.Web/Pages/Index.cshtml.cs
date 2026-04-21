using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Wh40kArmyEnricher.Web.Pages;

public class IndexModel : PageModel
{
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

        // TODO Session 5: enrich and store armies, redirect to ArmyView
        ErrorMessage = "Enrichment not yet implemented.";
        return Page();
    }
}
