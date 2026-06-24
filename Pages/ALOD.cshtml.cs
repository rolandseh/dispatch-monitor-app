using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using MiniSoftware;
using ZXing;
using ZXing.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MyModernWebApp.Pages
{
    public class ALODModel : PageModel
    {
        private readonly string _connString;
        private readonly IWebHostEnvironment _env;

        public ALODModel(Microsoft.Extensions.Configuration.IConfiguration configuration, IWebHostEnvironment env)
        {
            _connString = configuration.GetConnectionString("dispatcherConnectionString") ?? string.Empty;
            _env = env;
        }

        [BindProperty]
        public string SelectedBank { get; set; } = string.Empty;

        [BindProperty]
        public string SelectedStatus { get; set; } = "PENDING_LOD";

        public string SuccessMessage { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        public List<string> BankOptions { get; set; } = new() { "MBB", "SCB", "OCBC", "UOB" };

        public IActionResult OnGet()
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                return RedirectToPage("/Login");
            }
            return Page();
        }

        public async Task<IActionResult> OnPostGenerateAsync()
        {
            if (string.IsNullOrEmpty(SelectedBank))
            {
                ErrorMessage = "Please select a valid Client to process.";
                return Page();
            }

            try
            {
                byte[] finalZipBytes;
                int lettersGeneratedCount = 0;

                // Re-added the prefix filter so it only scans for the bank you selected
                string selectQuery = @"
                    SELECT 
                        c.intCaseID,
                        c.vchFileRefNumber AS OurRef,
                        ip.intInvolvedPersonID as PersonID,
                        ip.vchInvolvedPersonLName As PersonName,
                        ip.vchInvolvedPersonNric As PersonNRIC,
                        ipaddr.vchInvolvedPersonAddr1 As ADDR1,
                        ipaddr.vchInvolvedPersonAddr2 As ADDR2,
                        ipaddr.vchInvolvedPersonAddr3 As ADDR3,
                        ctip.intInvolvedPersonAccountID As InvolvedPersonAccountID,
                        ipacc.vchAccountNo As AccountNo,
                        ipacc.decLDAmt As LDAmt,
                        csi.vchCaseStatusType As CaseStatus
                    FROM dbo.tblCase c
                    INNER JOIN dbo.tblCaseToInvolvedPerson cip ON c.intcaseid = cip.intCaseID
                    INNER JOIN dbo.tblInvolvedPerson ip ON ip.intInvolvedPersonID = cip.intInvolvedPersonID
                    INNER JOIN dbo.tblInvolvedPersonAddr ipaddr on ipaddr.intInvolvedPersonID = ip.intInvolvedPersonID
                    INNER JOIN dbo.tblCasetoIPAccount ctip ON ctip.intCaseID = c.intCaseID
                    INNER JOIN dbo.tblInvolvedPersonAccount ipacc ON ipacc.intInvolvedPersonAccountID = ctip.intInvolvedPersonAccountID
                    INNER JOIN dbo.tblCaseStatusInstance csi ON csi.intCaseID = c.intCaseID
                    WHERE c.vchFileRefNumber LIKE @BankPrefix + '%'
                      AND c.dtLastUpdateDate >= CAST(GETDATE() AS DATE) 
                      AND c.dtLastUpdateDate < DATEADD(day, 1, CAST(GETDATE() AS DATE))
                      AND c.dtdateclosed IS NULL
                    ORDER BY csi.dtCSIUpdateDate DESC;";

                using (var zipOutputStream = new MemoryStream())
                {
                    using (var archive = new ZipArchive(zipOutputStream, ZipArchiveMode.Create, true))
                    {
                        using (var conn = new SqlConnection(_connString))
                        {
                            await conn.OpenAsync();

                            var linkedPeople = new List<dynamic>();
                            
                            using (var cmd = new SqlCommand(selectQuery, conn))
                            {
                                // Binds your dropdown value safely to the query
                                cmd.Parameters.AddWithValue("@BankPrefix", SelectedBank.Trim());

                                using (var reader = await cmd.ExecuteReaderAsync())
                                {
                                    while (await reader.ReadAsync())
                                    {
                                        string dbFileRef = reader.IsDBNull(reader.GetOrdinal("OurRef")) ? string.Empty : reader.GetString(reader.GetOrdinal("OurRef"));
                                        string a1 = reader.IsDBNull(reader.GetOrdinal("ADDR1")) ? string.Empty : reader.GetString(reader.GetOrdinal("ADDR1")).Trim();
                                        string a2 = reader.IsDBNull(reader.GetOrdinal("ADDR2")) ? string.Empty : reader.GetString(reader.GetOrdinal("ADDR2")).Trim();
                                        string a3 = reader.IsDBNull(reader.GetOrdinal("ADDR3")) ? string.Empty : reader.GetString(reader.GetOrdinal("ADDR3")).Trim();
                                        string accNo = reader.IsDBNull(reader.GetOrdinal("AccountNo")) ? string.Empty : reader.GetString(reader.GetOrdinal("AccountNo")).Trim();
                                        decimal ldAmt = reader.IsDBNull(reader.GetOrdinal("LDAmt")) ? 0m : reader.GetDecimal(reader.GetOrdinal("LDAmt"));

                                        linkedPeople.Add(new {
                                            CaseID = reader.GetInt32(reader.GetOrdinal("intCaseID")),
                                            OurRef = dbFileRef,
                                            PersonID = reader.GetInt32(reader.GetOrdinal("PersonID")),
                                            PersonName = reader.IsDBNull(reader.GetOrdinal("PersonName")) ? string.Empty : reader.GetString(reader.GetOrdinal("PersonName")),
                                            PersonNRIC = reader.IsDBNull(reader.GetOrdinal("PersonNRIC")) ? string.Empty : reader.GetString(reader.GetOrdinal("PersonNRIC")),
                                            AddressLine1 = a1,
                                            AddressLine2 = a2,
                                            AddressLine3 = a3,
                                            InvolvedPersonAccountID = reader.GetInt32(reader.GetOrdinal("InvolvedPersonAccountID")),
                                            AccountNo = accNo,
                                            LDAmt = ldAmt,
                                            CaseStatus = reader.IsDBNull(reader.GetOrdinal("CaseStatus")) ? string.Empty : reader.GetString(reader.GetOrdinal("CaseStatus"))
                                        });
                                    }
                                }
                            }

                            if (!linkedPeople.Any())
                            {
                                ErrorMessage = $"No active records were updated today for Bank '{SelectedBank}'.";
                                return Page();
                            }

                            foreach (var person in linkedPeople)
                            {
                                string fileRef = person.OurRef;
                                string accountNo = person.AccountNo;

                                byte[] barcodeBytes;
                                var barcodeWriter = new BarcodeWriterPixelData
                                {
                                    Format = BarcodeFormat.CODE_128,
                                    Options = new EncodingOptions { Height = 45, Width = 220, Margin = 0 }
                                };
                                var pixelData = barcodeWriter.Write(accountNo);
                                
                                using (var image = Image.LoadPixelData<Bgra32>(pixelData.Pixels, pixelData.Width, pixelData.Height))
                                {
                                    using (var imgMs = new MemoryStream())
                                    {
                                        await image.SaveAsPngAsync(imgMs);
                                        barcodeBytes = imgMs.ToArray();
                                    }
                                }

                                string tempBarcodePath = Path.Combine(Path.GetTempPath(), $"barcode_{Guid.NewGuid()}.png");
                                await System.IO.File.WriteAllBytesAsync(tempBarcodePath, barcodeBytes);

                                string bankPrefix = fileRef.Split('-').FirstOrDefault() ?? "GENERAL";
                                string templatePath;

                                if (bankPrefix.Equals("MBB", StringComparison.OrdinalIgnoreCase))
                                {
                                    templatePath = Path.Combine(_env.WebRootPath, "templates", "MBB", "LD TEMPLATE (BORROWER).docx");
                                    
                                    if (!System.IO.File.Exists(templatePath))
                                    {
                                        if (System.IO.File.Exists(tempBarcodePath)) System.IO.File.Delete(tempBarcodePath);
                                        ErrorMessage = $"Strict Rule Error: Could not find the required template at '{templatePath}' for MBB record {fileRef}.";
                                        return Page();
                                    }
                                }
                                else
                                {
                                    string dynamicTemplateFolder = Path.Combine(_env.WebRootPath, "templates", bankPrefix);
                                    templatePath = Path.Combine(dynamicTemplateFolder, "LD TEMPLATE (BORROWER).docx");

                                    if (!System.IO.File.Exists(templatePath))
                                    {
                                        templatePath = Path.Combine(_env.WebRootPath, "templates", "LD TEMPLATE (BORROWER).docx");
                                    }
                                    
                                    if (!System.IO.File.Exists(templatePath))
                                    {
                                        if (System.IO.File.Exists(tempBarcodePath)) System.IO.File.Delete(tempBarcodePath);
                                        ErrorMessage = $"Template missing: Unable to process {fileRef}. Checked '{dynamicTemplateFolder}' and fallback configurations.";
                                        return Page();
                                    }
                                }

                                try
                                {
                                    byte[] documentBytes;
                                    var valueMap = new Dictionary<string, object>
                                    {
                                        { "OurRef", $"Our Ref: {fileRef}" },
                                        { "DATE", DateTime.Now.ToString("dd MMMM yyyy").ToUpper() }, 
                                        { "NAME", $"{person.PersonName} ({person.PersonNRIC})" },
                                        { "ADDR1", person.AddressLine1 }, 
                                        { "ADDR2", person.AddressLine2 }, 
                                        { "ADDR3", person.AddressLine3 }, 
                                        { "AccountNo", accountNo },
                                        { "LDAmt", person.LDAmt.ToString("N2") }, 
                                        { "CaseStatus", person.CaseStatus },
                                        { "Barcode", new MiniWordPicture { Path = tempBarcodePath, Width = 220, Height = 45 } }
                                    };

                                    using (var docStream = new MemoryStream())
                                    {
                                        MiniWord.SaveAsByTemplate(docStream, templatePath, valueMap);
                                        documentBytes = docStream.ToArray();
                                    }

                                    string safePersonName = string.Concat(((string)person.PersonName).Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
                                    var zipEntry = archive.CreateEntry($"LOD_{bankPrefix}_{accountNo}_{safePersonName}.docx");
                                    using (var entryStream = zipEntry.Open())
                                    {
                                        await entryStream.WriteAsync(documentBytes, 0, documentBytes.Length);
                                    }

                                    lettersGeneratedCount++;
                                }
                                finally
                                {
                                    if (System.IO.File.Exists(tempBarcodePath)) System.IO.File.Delete(tempBarcodePath);
                                }
                            }
                        }
                    }
                    finalZipBytes = zipOutputStream.ToArray();
                }

                ModelState.Clear();
                return File(finalZipBytes, "application/zip", $"LOD_ScanBatch_{SelectedBank}_{DateTime.Now:yyyyMMdd}.zip");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Processing Exception: {ex.Message}";
                return Page();
            }
        }
    }
}