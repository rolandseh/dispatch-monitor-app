using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace MyModernWebApp.Pages
{
    public class SettingsModel : PageModel
    {
        private readonly string _connString;

        public SettingsModel(IConfiguration configuration)
        {
            _connString = configuration.GetConnectionString("dispatcherConnectionString") ?? string.Empty;
        }

        public List<UserItem> UsersList { get; set; } = new List<UserItem>();
        public List<PostalItem> PostalList { get; set; } = new List<PostalItem>();

        [BindProperty]
        public UserItem NewUser { get; set; } = new UserItem();

        [BindProperty]
        public UserItem EditUser { get; set; } = new UserItem();

        public string? EditingUsername { get; set; }

        [BindProperty]
        public PostalItem NewPostal { get; set; } = new PostalItem();

        [BindProperty]
        public PostalItem EditPostal { get; set; } = new PostalItem();

        public int? EditingPostalId { get; set; }

        public IActionResult OnGet(string? editPostalId, string? editUsername)
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("Username")))
            {
                return RedirectToPage("/Login");
            }

            if (!string.IsNullOrEmpty(editPostalId))
            {
                EditingPostalId = Convert.ToInt32(editPostalId);
            }

            if (!string.IsNullOrEmpty(editUsername))
            {
                EditingUsername = editUsername;
            }

            LoadUsers();
            LoadPostalData();

            return Page();
        }

        private void LoadUsers()
        {
            using (SqlConnection conn = new SqlConnection(_connString))
            {
                string query = "SELECT username, name, admin, deviceid, enabled FROM tbl_D_users";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    conn.Open();
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string adminVal = rdr["admin"]?.ToString()?.Trim() ?? "";
                            string enabledVal = rdr["enabled"]?.ToString()?.Trim() ?? "";

                            UsersList.Add(new UserItem {
                                Username = rdr["username"]?.ToString() ?? string.Empty,
                                Name = rdr["name"]?.ToString() ?? string.Empty,
                                Admin = adminVal.Equals("Y", StringComparison.OrdinalIgnoreCase) || adminVal.Equals("True", StringComparison.OrdinalIgnoreCase),
                                DeviceId = rdr["deviceid"]?.ToString() ?? string.Empty,
                                Enabled = enabledVal.Equals("Y", StringComparison.OrdinalIgnoreCase) || enabledVal.Equals("True", StringComparison.OrdinalIgnoreCase)
                            });
                        }
                    }
                }
            }
        }

        private void LoadPostalData()
        {
            using (SqlConnection conn = new SqlConnection(_connString))
            {
                string query = "SELECT id, district, sector, location FROM tbl_D_postal ORDER BY id DESC";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    conn.Open();
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            PostalList.Add(new PostalItem {
                                Id = Convert.ToInt32(rdr["id"]),
                                District = rdr["district"]?.ToString() ?? string.Empty,
                                Sector = rdr["sector"]?.ToString() ?? string.Empty,
                                Location = rdr["location"]?.ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
        }

        public IActionResult OnPostAddUser()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("Username"))) return RedirectToPage("/Login");
            if (!ModelState.IsValid) return RedirectToPage();

            try
            {
                using (SqlConnection conn = new SqlConnection(_connString))
                {
                    string query = "INSERT INTO tbl_D_users (username, name, passwd, admin, deviceid, enabled) VALUES (@user, @name, @pass, @admin, @dev, @en)";
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@user", NewUser.Username);
                        cmd.Parameters.AddWithValue("@name", NewUser.Name);
                        cmd.Parameters.AddWithValue("@pass", NewUser.Passwd);
                        cmd.Parameters.AddWithValue("@admin", NewUser.Admin ? "Y" : "N");
                        cmd.Parameters.AddWithValue("@dev", string.IsNullOrEmpty(NewUser.DeviceId) ? "" : NewUser.DeviceId);
                        cmd.Parameters.AddWithValue("@en", NewUser.Enabled ? "Y" : "N");

                        conn.Open();
                        cmd.ExecuteNonQuery();
                    }
                }
                return RedirectToPage("/Settings");
            }
            catch (SqlException ex)
            {
                return Content($"Database Refused Insert! Error Details: {ex.Message}");
            }
        }

        public IActionResult OnPostDeleteUser(string username)
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("Username"))) return RedirectToPage("/Login");

            using (SqlConnection conn = new SqlConnection(_connString))
            {
                string query = "DELETE FROM tbl_D_users WHERE username = @user";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@user", username);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            return RedirectToPage("/Settings");
        }

        public IActionResult OnPostUpdateUser()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("Username"))) return RedirectToPage("/Login");

            using (SqlConnection conn = new SqlConnection(_connString))
            {
                string query = @"UPDATE tbl_D_users 
                                 SET name = @name, 
                                     admin = @admin, 
                                     deviceid = @dev, 
                                     enabled = @en" 
                                 + (!string.IsNullOrEmpty(EditUser.Passwd) ? ", passwd = @pass " : " ") +
                                 "WHERE username = @user";

                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@user", EditUser.Username);
                    cmd.Parameters.AddWithValue("@name", EditUser.Name);
                    cmd.Parameters.AddWithValue("@admin", EditUser.Admin ? "Y" : "N");
                    cmd.Parameters.AddWithValue("@dev", string.IsNullOrEmpty(EditUser.DeviceId) ? "" : EditUser.DeviceId);
                    cmd.Parameters.AddWithValue("@en", EditUser.Enabled ? "Y" : "N");

                    if (!string.IsNullOrEmpty(EditUser.Passwd))
                    {
                        cmd.Parameters.AddWithValue("@pass", EditUser.Passwd);
                    }

                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }

            return RedirectToPage(new { activeTab = "users" });
        }

        public IActionResult OnPostAddPostal()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("Username"))) return RedirectToPage("/Login");

            using (SqlConnection conn = new SqlConnection(_connString))
            {
                string query = "INSERT INTO tbl_D_postal (district, sector, location) VALUES (@dist, @sec, @loc)";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@dist", NewPostal.District);
                    cmd.Parameters.AddWithValue("@sec", NewPostal.Sector);
                    cmd.Parameters.AddWithValue("@loc", NewPostal.Location);

                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            return RedirectToPage(new { activeTab = "postal" });
        }

        public IActionResult OnPostUpdatePostal()
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("Username"))) return RedirectToPage("/Login");

            using (SqlConnection conn = new SqlConnection(_connString))
            {
                string query = "UPDATE tbl_D_postal SET district = @dist, sector = @sec, location = @loc WHERE id = @id";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", EditPostal.Id);
                    cmd.Parameters.AddWithValue("@dist", EditPostal.District);
                    cmd.Parameters.AddWithValue("@sec", EditPostal.Sector);
                    cmd.Parameters.AddWithValue("@loc", EditPostal.Location);

                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            return RedirectToPage(new { activeTab = "postal" });
        }

        public IActionResult OnPostDeletePostal(int id)
        {
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("Username"))) return RedirectToPage("/Login");

            using (SqlConnection conn = new SqlConnection(_connString))
            {
                string query = "DELETE FROM tbl_D_postal WHERE id = @id";
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            return RedirectToPage(new { activeTab = "postal" });
        }
    }

    public class UserItem
    {
        public string Username { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Passwd { get; set; } = string.Empty;
        public bool Admin { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    public class PostalItem
    {
        public int Id { get; set; }
        public string District { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }
}