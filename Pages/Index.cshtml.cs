using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DispatcherFrontEnd.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
            // Instantly redirect anyone who hits http://localhost:8080/ to http://localhost:8080/Login
            return RedirectToPage("/Login");
    }

}
