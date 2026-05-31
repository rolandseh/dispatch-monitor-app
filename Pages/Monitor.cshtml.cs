using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;

namespace MyModernWebApp.Pages
{
    public class MonitorModel : PageModel
    {
        private readonly IConfiguration _configuration;

        public MonitorModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // These properties hold the data that your HTML frontend loops through
        public List<DispatchItem> CurrentDayDispatch { get; set; } = new List<DispatchItem>();
        public List<DispatchItem> OverdueDispatch { get; set; } = new List<DispatchItem>();

        public IActionResult OnGet()
        {
            // 1. Check if the user has an active, valid login session key
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                // 2. Not logged in? Kick them back to the login screen immediately
                return RedirectToPage("/Login");
            }

            // 3. Authorized! Go ahead and pull data out of the database
            LoadBindData();
            
            return Page();
        }

        private void LoadBindData()
        {
            string connString = _configuration.GetConnectionString("dispatcherConnectionString") ?? string.Empty;

            // 1. Fetch Current Day Dispatch Data
            CurrentDayDispatch = FetchDispatchData(connString, "SELECT caserefno, debtorname, debtoraddr FROM tbl_D_dispatchletter");

            // 2. Fetch Overdue Dispatch Data 
            OverdueDispatch = FetchDispatchData(connString, "SELECT caserefno, debtorname, debtoraddr FROM tbl_D_dispatchletter");
        }

        private List<DispatchItem> FetchDispatchData(string connectionString, string query)
        {
            var list = new List<DispatchItem>();

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        conn.Open();
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new DispatchItem
                                {
                                    CaseRefNo = reader["caserefno"]?.ToString() ?? string.Empty,
                                    DebtorName = reader["debtorname"]?.ToString() ?? string.Empty,
                                    DebtorAddr = reader["debtoraddr"]?.ToString() ?? string.Empty
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database Error: {ex.Message}");
            }

            return list;
        }
    }

    public class DispatchItem
    {
        public string CaseRefNo { get; set; } = string.Empty;
        public string DebtorName { get; set; } = string.Empty;
        public string DebtorAddr { get; set; } = string.Empty;
    }
}