using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

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

        public void OnGet()
        {
            // Leave open so users can always see the login screen
        }

        public IActionResult OnPost(string username, string password)
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
                            // 1. Set the session flags to lock the application down
                            HttpContext.Session.SetString("IsLoggedIn", "true");
                            HttpContext.Session.SetString("Username", username);

                            // 2. Send them straight to the Monitor dashboard dashboard
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