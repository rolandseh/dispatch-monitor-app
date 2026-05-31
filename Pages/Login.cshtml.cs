using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authentication; // 👈 CRITICAL: Added for cookie authentication
using System.Security.Claims;               // 👈 CRITICAL: Added for Claims identity

namespace MyModernWebApp.Pages
{
    public class LoginModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public LoginModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [BindProperty]
        public string ErrorMessage { get; set; } = string.Empty;

        // Handles Logging Out when clicking the navbar sign out button
        public async Task<IActionResult> OnGetLogoutAsync()
        {
            await HttpContext.SignOutAsync("FirmAuthCookie");
            return RedirectToPage("/Login");
        }

        public void OnGet()
        {
            // Old Session.Clear() code removed entirely to stop crashes
        }

        public async Task<IActionResult> OnPostAsync(string username, string password)
        {
            string connString = _configuration.GetConnectionString("dispatcherConnectionString") ?? string.Empty;
            string cmd = "SELECT COUNT(1) from tbl_D_users where username = @Username AND passwd = @Password";

            try
            {
                using (SqlConnection connection = new SqlConnection(connString))
                {
                    using (SqlCommand command = new SqlCommand(cmd, connection))
                    {
                        command.Parameters.AddWithValue("@Username", username);
                        command.Parameters.AddWithValue("@Password", password);

                        connection.Open();
                        int userExists = Convert.ToInt32(command.ExecuteScalar());

                        if (userExists > 0)
                        {
                            string fullName = ""; 

                            using (SqlConnection conn = new SqlConnection(connString))
                            {
                                string query = "SELECT name FROM tbl_D_users WHERE username = @user";
                                using (SqlCommand sqlCmd = new SqlCommand(query, conn))
                                {
                                    sqlCmd.Parameters.AddWithValue("@user", username);
                                    conn.Open();
                                    fullName = sqlCmd.ExecuteScalar()?.ToString() ?? username;
                                }
                            }

                            // 🌟 MODERN COOKIE AUTHENTICATION REPLACES BROKEN SESSIONS
                            var claims = new List<Claim>
                            {
                                // This sets the name that your updated _Layout.cshtml reads via @User.Identity.Name
                                new Claim(ClaimTypes.Name, fullName), 
                                new Claim("UsernameString", username)
                            };

                            var claimsIdentity = new ClaimsIdentity(claims, "FirmAuthCookie");
                            var authProperties = new AuthenticationProperties
                            {
                                IsPersistent = true,
                                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
                            };

                            // Bake the secure encrypted browser cookie right now
                            await HttpContext.SignInAsync("FirmAuthCookie", new ClaimsPrincipal(claimsIdentity), authProperties);

                            return RedirectToPage("/Monitor"); 
                        }
                    }
                }
                
                ErrorMessage = "Invalid username or password.";
                return Page();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Database error: {ex.Message}";
                return Page();
            }
        }
    }
}