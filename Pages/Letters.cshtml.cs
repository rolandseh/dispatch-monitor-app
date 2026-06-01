using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using MiniExcelLibs;
using MiniSoftware;
using ZXing;
using ZXing.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MyModernWebApp.Pages
{
    public class LettersModel : PageModel
    {
        private readonly string _connString;
        private readonly IWebHostEnvironment _env;

        public LettersModel(Microsoft.Extensions.Configuration.IConfiguration configuration, IWebHostEnvironment env)
        {
            _connString = configuration.GetConnectionString("dispatcherConnectionString") ?? string.Empty;
            _env = env;
        }

        [BindProperty]
        public string SuccessMessage { get; set; } = string.Empty;

        [BindProperty]
        public string ErrorMessage { get; set; } = string.Empty;

        public IActionResult OnGet()
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                return RedirectToPage("/Login");
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(IFormFile excelFile)
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated) return RedirectToPage("/Login");

            if (excelFile == null || excelFile.Length == 0)
            {
                ErrorMessage = "Please select a valid Excel file to process.";
                return Page();
            }

            try
            {
                byte[] finalZipBytes;
                int lettersGeneratedCount = 0; 

                using (var excelStream = new MemoryStream())
                {
                    await excelFile.CopyToAsync(excelStream);
                    excelStream.Position = 0;

                    var rows = excelStream.Query(useHeaderRow: true).Cast<IDictionary<string, object>>().ToList();
                    if (!rows.Any())
                    {
                        ErrorMessage = "Excel file is empty or missing a header row.";
                        return Page();
                    }

                    using (var zipOutputStream = new MemoryStream())
                    {
                        using (var archive = new ZipArchive(zipOutputStream, ZipArchiveMode.Create, true))
                        {
                            using (var conn = new SqlConnection(_connString))
                            {
                                await conn.OpenAsync();

                                foreach (var row in rows)
                                {
                                    string accountNo = string.Empty;
                                    var accountKey = row.Keys.FirstOrDefault(k => k.Equals("AccountNo", StringComparison.OrdinalIgnoreCase));
                                    if (accountKey != null)
                                    {
                                        accountNo = row[accountKey]?.ToString()?.Trim() ?? string.Empty;
                                    }

                                    if (string.IsNullOrEmpty(accountNo)) continue;

                                    // 1. Relational Read Query 
                                    string selectQuery = @"
                                        SELECT 
                                            c.intCaseID,
                                            c.vchFileRefNumber AS OurRef,
                                            ip.vchInvolvedPersonLName As PersonName,
                                            ip.vchInvolvedPersonNric As PersonNRIC,
                                            ipaddr.vchInvolvedPersonAddr1 As Addr1,
                                            ipaddr.vchInvolvedPersonAddr2 As Addr2,
                                            ipaddr.vchInvolvedPersonAddr3 As Addr3
                                        FROM dbo.tblCase c
                                        INNER JOIN dbo.tblCaseToInvolvedPerson cip ON c.intcaseid = cip.intCaseID
                                        INNER JOIN dbo.tblInvolvedPerson ip ON ip.intInvolvedPersonID = cip.intInvolvedPersonID
                                        INNER JOIN dbo.tblInvolvedPersonAddr ipaddr on ipaddr.intInvolvedPersonID = cip.intInvolvedPersonID
                                        WHERE c.vchFileRefNumber = @AccountNo";

                                    var linkedPeople = new List<dynamic>();
                                    int currentCaseId = 0;
                                    string dbFileRef = "";

                                    using (var cmd = new SqlCommand(selectQuery, conn))
                                    {
                                        cmd.Parameters.AddWithValue("@AccountNo", accountNo);
                                        using (var reader = await cmd.ExecuteReaderAsync())
                                        {
                                            while (await reader.ReadAsync())
                                            {
                                                currentCaseId = reader.GetInt32(reader.GetOrdinal("intCaseID"));
                                                dbFileRef = reader.IsDBNull(reader.GetOrdinal("OurRef")) ? string.Empty : reader.GetString(reader.GetOrdinal("OurRef"));
                                                
                                                linkedPeople.Add(new {
                                                    PersonName = reader.IsDBNull(reader.GetOrdinal("PersonName")) ? string.Empty : reader.GetString(reader.GetOrdinal("PersonName")),
                                                    PersonNRIC = reader.IsDBNull(reader.GetOrdinal("PersonNRIC")) ? string.Empty : reader.GetString(reader.GetOrdinal("PersonNRIC")),
                                                    AddressLine1 = reader.IsDBNull(reader.GetOrdinal("Addr1")) ? string.Empty : reader.GetString(reader.GetOrdinal("Addr1")).Trim(),
                                                    AddressLine2 = reader.IsDBNull(reader.GetOrdinal("Addr2")) ? string.Empty : reader.GetString(reader.GetOrdinal("Addr2")).Trim(),
                                                    AddressLine3 = reader.IsDBNull(reader.GetOrdinal("Addr3")) ? string.Empty : reader.GetString(reader.GetOrdinal("Addr3")).Trim()
                                                });
                                            }
                                        }
                                    }

                                    if (currentCaseId == 0 || !linkedPeople.Any()) continue; 

                                    // 2. Draw barcode cleanly via pure managed C# ImageSharp engine
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

                                    // 3. Determine Prefix and dynamic template paths
                                    string prefix = "DEFAULT";
                                    string letterPart = new string(accountNo.TakeWhile(char.IsLetter).ToArray()).ToUpper();
                                    if (!string.IsNullOrEmpty(letterPart))
                                    {
                                        prefix = letterPart;
                                    }

                                    string dynamicTemplateFolder = Path.Combine(_env.WebRootPath, "templates", prefix);
                                    string templatePath = Path.Combine(dynamicTemplateFolder, "LD TEMPLATE (BORROWER).docx");

                                    if (!System.IO.File.Exists(templatePath))
                                    {
                                        templatePath = Path.Combine(_env.WebRootPath, "templates", "LD TEMPLATE (BORROWER).docx");
                                        if (!System.IO.File.Exists(templatePath))
                                        {
                                            if (System.IO.File.Exists(tempBarcodePath)) System.IO.File.Delete(tempBarcodePath);
                                            ErrorMessage = $"Template missing! Checked: templates/{prefix}/ and root templates/ directory.";
                                            return Page();
                                        }
                                    }

                                    try
                                    {
                                        // 4. Generate separate documents for EVERY linked entity found
                                        foreach (var person in linkedPeople)
                                        {
                                            byte[] documentBytes;
                                            
                                            // Mapping to three separate keys matching your template file fields
                                            var valueMap = new Dictionary<string, object>
                                            {
                                                { "OurRef", $"Our Ref: {dbFileRef}" },
                                                { "DATE", DateTime.Now.ToString("dd MMMM yyyy").ToUpper() },
                                                { "NAME", $"{person.PersonName} ({person.PersonNRIC})" },
                                                { "Addr1", person.AddressLine1 }, // 👈 Maps to {{Addr1}}
                                                { "Addr2", person.AddressLine2 }, // 👈 Maps to {{Addr2}}
                                                { "Addr3", person.AddressLine3 }, // 👈 Maps to {{Addr3}}
                                                { "Barcode", new MiniWordPicture 
                                                    { 
                                                        Path = tempBarcodePath, 
                                                        Width = 220, 
                                                        Height = 45 
                                                    } 
                                                }
                                            };

                                            using (var docStream = new MemoryStream())
                                            {
                                                MiniWord.SaveAsByTemplate(docStream, templatePath, valueMap);
                                                documentBytes = docStream.ToArray();
                                            }

                                            // 5. Package output safely inside archive zip block
                                            string safePersonName = string.Concat(((string)person.PersonName).Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
                                            var zipEntry = archive.CreateEntry($"Case_{dbFileRef}_{safePersonName}_{person.PersonNRIC}.docx");
                                            using (var entryStream = zipEntry.Open())
                                            {
                                                await entryStream.WriteAsync(documentBytes, 0, documentBytes.Length);
                                            }

                                            lettersGeneratedCount++;
                                        }
                                    }
                                    finally
                                    {
                                        if (System.IO.File.Exists(tempBarcodePath))
                                        {
                                            System.IO.File.Delete(tempBarcodePath);
                                        }
                                    }
                                }
                            }
                        } 
                        finalZipBytes = zipOutputStream.ToArray();
                    }
                }

                if (lettersGeneratedCount == 0)
                {
                    ErrorMessage = "The process finished, but 0 letters were generated. Verify your input account numbers exist inside your SQL tables.";
                    return Page();
                }

                ModelState.Clear(); 
                return File(finalZipBytes, "application/zip", $"MailMerge_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Processing Exception: {ex.Message}";
                return Page();
            }
        }
    }
}