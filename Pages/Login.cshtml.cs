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
            // Clear out the session data completely on load
            HttpContext.Session.Clear();
    
            // Optional: If you want to be absolutely sure, remove the key specifically
            HttpContext.Session.Remove("Username");
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

                            string fullName = ""; 

                            using (SqlConnection conn = new SqlConnection(connString))
                            {
                                // Fetch the actual display name for this specific username
                                string query = "SELECT name FROM tbl_D_users WHERE username = @user";
                                using (SqlCommand sqlCmd = new SqlCommand(query, conn))
                                {
                                    sqlCmd.Parameters.AddWithValue("@user", username);
                                    conn.Open();
                                    fullName = sqlCmd.ExecuteScalar()?.ToString() ?? username; // Fallback to username if blank
                                }
                            }


                            // 1. Set the session flags to lock the application down
                            HttpContext.Session.SetString("IsLoggedIn", "true");
                            HttpContext.Session.SetString("Username", username);
                            HttpContext.Session.SetString("UserFullName", fullName);

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