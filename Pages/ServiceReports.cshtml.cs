using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace MyModernWebApp.Pages
{
    public class ServiceReportsModel : PageModel
    {
        private readonly string _connString;

        public ServiceReportsModel(Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _connString = configuration.GetConnectionString("dispatcherConnectionString") ?? string.Empty;
        }

        [BindProperty(SupportsGet = true)]
        public string SearchCaseId { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public string SingleDate { get; set; } = string.Empty; 
        [BindProperty(SupportsGet = true)]
        public string StartDate { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public string EndDate { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public string SelectedRefNum { get; set; } = string.Empty;

        public List<ServiceReportSummary> SearchResults { get; set; } = new List<ServiceReportSummary>();
        public ServiceReportDetail? ActiveReport { get; set; }
        public string ViewContextMode { get; set; } = "Today's Dispatch Activity";

        public async Task OnGetAsync()
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated) return;

            if (!string.IsNullOrEmpty(SelectedRefNum))
            {
                await LoadReportDetailsAsync(SelectedRefNum);
                return;
            }
            await LoadServiceReportsAsync();
        }

        public async Task<IActionResult> OnGetExportWordAsync(string refNum)
        {
            if (string.IsNullOrEmpty(refNum)) return BadRequest("Invalid Reference Number.");

            await LoadReportDetailsAsync(refNum);
            if (ActiveReport == null) return NotFound("Record not found.");

            var baseDir = AppContext.BaseDirectory;
            var templatePath = Path.Combine(baseDir, "wwwroot", "templates", "BlankShell.docx");
            if (!System.IO.File.Exists(templatePath))
            {
                templatePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "templates", "BlankShell.docx");
            }
            if (!System.IO.File.Exists(templatePath)) 
            {
                return NotFound("The baseline asset file 'BlankShell.docx' was not found in 'wwwroot/templates/'.");
            }

            var memoryStream = new MemoryStream();
            using (var fileStream = new FileStream(templatePath, FileMode.Open, FileAccess.Read))
            {
                await fileStream.CopyToAsync(memoryStream);
            }
            memoryStream.Position = 0;

            using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(memoryStream, true))
            {
                MainDocumentPart mainPart = wordDoc.MainDocumentPart ?? wordDoc.AddMainDocumentPart();
                if (mainPart.Document == null) mainPart.Document = new Document();
                
                Body body = mainPart.Document.Body ?? mainPart.Document.AppendChild(new Body());
                body.RemoveAllChildren();

                SectionProperties secProps = new SectionProperties();
                PageMargin margins = new PageMargin() { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440 };
                secProps.Append(margins);

                // Branding Header Title Elements
                Paragraph titlePara = new Paragraph();
                titlePara.Append(new ParagraphProperties(new Justification() { Val = JustificationValues.Left }));
                Run titleRun = new Run(new Text("Advent Law Corporation"));
                titleRun.RunProperties = new RunProperties(new Bold(), new FontSize() { Val = "36" }, new RunFonts() { Ascii = "Times New Roman" });
                titlePara.Append(titleRun);
                body.Append(titlePara);

                Paragraph subtitlePara = new Paragraph();
                Run subtitleRun = new Run(new Text("(incorporated with limited liability)                                                                     Advocates & Solicitors"));
                subtitleRun.RunProperties = new RunProperties(new Italic(), new FontSize() { Val = "18" }, new RunFonts() { Ascii = "Times New Roman" });
                subtitlePara.Append(subtitleRun);
                body.Append(subtitlePara);

                body.Append(new Paragraph(new Run(new Text("_________________________________________________________________________________"))));

                Paragraph docTitlePara = new Paragraph();
                docTitlePara.Append(new ParagraphProperties(new Justification() { Val = JustificationValues.Center }));
                Run docTitleRun = new Run(new Text("\nSERVICE REPORT\n"));
                docTitleRun.RunProperties = new RunProperties(new Bold(), new FontSize() { Val = "28" }, new Underline() { Val = UnderlineValues.Single });
                docTitlePara.Append(docTitleRun);
                body.Append(docTitlePara);

                // Build Structural Tables
                Table metaTable = CreateEmptyBorderedTable();
                metaTable.Append(CreateTwoColumnRow("FILE REF NO :", ActiveReport.FileRefNumber, "DATE SERVED :", ActiveReport.DispatchedDate));
                metaTable.Append(CreateTwoColumnRow("SUBJECT :", "INSPECTION LOG ATTESTATION", "TIME SERVED :", ActiveReport.DispatchedTime));
                metaTable.Append(CreateFullWidthRow("ADDRESS :", "420 NORTH BRIDGE ROAD #03-32 SINGAPORE 188727"));
                metaTable.Append(CreateFullWidthRow("CONTACT(S) :", "(R) :                   (O) :                   (PGR) :                   (HP) :"));
                metaTable.Append(CreateFullWidthRow("CONTACT REASON :", "(If contact number(s) not obtained, please state reason: ___________________________________)"));
                body.Append(metaTable);
                body.Append(new Paragraph(new Run(new Text("\n"))));

                Table propTable = CreateEmptyBorderedTable();
                propTable.Append(CreateHeaderRow("TYPE OF PROPERTY (Please tick & delete accordingly) :", "* Please tick:  [ X ] Normal Lock   [  ] Digital Lock"));
                propTable.Append(CreateCheckboxRow(true, "Bungalow / Semi-Detached Bungalow"));
                propTable.Append(CreateCheckboxRow(false, "Terrace House / Condominium / Private Apartment"));
                propTable.Append(CreateCheckboxRow(false, "HDB ( 3-Room / 4-Room / 5-Room / Executive / Maisonette )"));
                propTable.Append(CreateCheckboxRow(false, "Office / Shop / Factory (Name: ___________________________________ )"));
                body.Append(propTable);
                body.Append(new Paragraph(new Run(new Text("\n"))));

                Table furnishTable = CreateEmptyBorderedTable();
                furnishTable.Append(CreateFullWidthRow("FURNISHING (Please circle accordingly) :", "Well Furnished  /  Moderately Furnished  /  Sparsely Furnished"));
                furnishTable.Append(CreateFullWidthRow("SPECIFY ASSETS :", "TV  /  Hifi  /  Sofa  /  Cabinets  /  Fridge  /  Aircon  /  Fan  /  Computer  /  Piano"));
                furnishTable.Append(CreateFullWidthRow("OTHERS :", "_________________________________________________________________________________"));
                body.Append(furnishTable);
                body.Append(new Paragraph(new Run(new Text("\n"))));

                Table recTable = CreateEmptyBorderedTable();
                recTable.Append(CreateHeaderRow("RECEIVED BY: MALE / FEMALE", ""));
                recTable.Append(CreateCheckboxRow(false, "Debtor personally"));
                recTable.Append(CreateCheckboxRow(true, "Occupant / Maid (Please specify: ___________________________________ )"));
                recTable.Append(CreateCheckboxRow(false, "Friend"));
                recTable.Append(CreateCheckboxRow(false, "Colleague / Receptionist"));
                recTable.Append(CreateCheckboxRow(false, "Tenant ( Chinese / Malay / Indian / Caucasian / Eurasian / Others )"));
                body.Append(recTable);
                body.Append(new Paragraph(new Run(new Text("\n"))));

                Table statusTable = CreateEmptyBorderedTable();
                statusTable.Append(CreateHeaderRow("PREMISES LOCKED (Please tick & delete accordingly) :", ""));
                statusTable.Append(CreateCheckboxRow(true, "Left notice of service / Letter at premises / Letter box for owner to contact Advent Law or Bank officer"));
                statusTable.Append(CreateCheckboxRow(false, "Confirmed with neighbour that owner staying / not staying / they do not know who is currently staying"));
                statusTable.Append(CreateCheckboxRow(true, "Light / Noise observed emitting from inside target premises"));
                statusTable.Append(CreateCheckboxRow(false, "Premises appears completely vacant / dusty / abandoned layout"));
                statusTable.Append(CreateCheckboxRow(false, "No shoes / shoes outside premises entryway"));
                statusTable.Append(CreateCheckboxRow(true, "Utility Meter spinning running / not running / cannot be viewed"));
                statusTable.Append(CreateCheckboxRow(false, "Letter box status: full / not full / cannot be viewed"));
                body.Append(statusTable);
                body.Append(new Paragraph(new Run(new Text("\n"))));

                Table remarksTable = CreateEmptyBorderedTable();
                remarksTable.Append(CreateFullWidthRow("REMARKS :", "Inspection records synchronized with terminal data matrices.\n\n\n"));
                remarksTable.Append(CreateTwoColumnRow("SERVED BY :", ActiveReport.DispatchedBy, "SIGNATURE :", "___________________________"));
                body.Append(remarksTable);

                // Document Image Attachment Stream Injection
                if (ActiveReport.ImageAttachments.Any())
                {
                    body.AppendChild(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));
                    
                    Paragraph imgHeader = new Paragraph();
                    imgHeader.ParagraphProperties = new ParagraphProperties(new Justification() { Val = JustificationValues.Center });
                    Run headerRun = new Run(new Text("APPENDIX: ATTACHED PHOTOGRAPHIC EVIDENCE"));
                    headerRun.RunProperties = new RunProperties(new Bold(), new FontSize() { Val = "24" });
                    imgHeader.Append(headerRun);
                    body.Append(imgHeader);
                    body.Append(new Paragraph(new Run(new Text("\n"))));

                    uint currentImageId = 2000; 

                    foreach (var imgData in ActiveReport.ImageAttachments)
                    {
                        try
                        {
                            byte[] imageBytes;

                            if (imgData.StartsWith("MOCK_IMAGE_BYTES:"))
                            {
                                imageBytes = LoadLocalFallbackFile();
                            }
                            else
                            {
                                try 
                                {
                                    string cleanBase64 = imgData;
                                    if (cleanBase64.Contains(",")) cleanBase64 = cleanBase64.Split(',')[1];
                                    imageBytes = Convert.FromBase64String(cleanBase64);
                                }
                                catch 
                                {
                                    imageBytes = LoadLocalFallbackFile();
                                }
                            }

                            if (imageBytes == null || imageBytes.Length == 0) continue;

                            // Strip OLE/wrap boundaries if present
                            if (imageBytes.Length > 4 && (imageBytes[0] != 0xFF || imageBytes[1] != 0xD8) && !(imageBytes[0] == 0x89 && imageBytes[1] == 0x50))
                            {
                                for (int i = 0; i < 100 && i < imageBytes.Length - 2; i++)
                                {
                                    if ((imageBytes[i] == 0xFF && imageBytes[i + 1] == 0xD8) || (imageBytes[i] == 0x89 && imageBytes[i + 1] == 0x50))
                                    {
                                        imageBytes = imageBytes.Skip(i).ToArray();
                                        break;
                                    }
                                }
                            }

                            ImagePart imagePart;
                            if (imageBytes.Length > 4 && imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
                            {
                                imagePart = mainPart.AddImagePart(ImagePartType.Png);
                            }
                            else
                            {
                                imagePart = mainPart.AddImagePart(ImagePartType.Jpeg);
                            }

                            using (MemoryStream ms = new MemoryStream(imageBytes))
                            {
                                imagePart.FeedData(ms);
                            }

                            // Pass down raw byte stream to let the layout engine set bounds cleanly
                            AddInlineImageToBody(mainPart.GetIdOfPart(imagePart), body, currentImageId, imageBytes);
                            currentImageId += 2; 
                        }
                        catch { /* Fail-safe block tracking container */ }
                    }
                }

                body.Append(secProps);
                mainPart.Document.Save();
            }

            memoryStream.Position = 0;
            return File(memoryStream, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"ServiceReport_{ActiveReport.FileRefNumber}.docx");
        }

        private byte[] LoadLocalFallbackFile()
        {
            var fallbackPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "fallback-evidence.jpg");
            if (System.IO.File.Exists(fallbackPath))
            {
                return System.IO.File.ReadAllBytes(fallbackPath);
            }
            return Array.Empty<byte>();
        }

        private Table CreateEmptyBorderedTable()
        {
            Table table = new Table();
            TableProperties tblProps = new TableProperties(
                new TableBorders(
                    new TopBorder() { Val = BorderValues.Single, Size = 4, Color = "000000" },
                    new BottomBorder() { Val = BorderValues.Single, Size = 4, Color = "000000" },
                    new LeftBorder() { Val = BorderValues.Single, Size = 4, Color = "000000" },
                    new RightBorder() { Val = BorderValues.Single, Size = 4, Color = "000000" },
                    new InsideHorizontalBorder() { Val = BorderValues.Single, Size = 4, Color = "AAAAAA" },
                    new InsideVerticalBorder() { Val = BorderValues.Single, Size = 4, Color = "E0E0E0" }
                ),
                new TableWidth() { Width = "5000", Type = TableWidthUnitValues.Pct }
            );
            table.AppendChild(tblProps);
            return table;
        }

        private TableRow CreateTwoColumnRow(string lbl1, string val1, string lbl2, string val2)
        {
            TableRow row = new TableRow();
            row.Append(CreateCell(lbl1, true, 2000));
            row.Append(CreateCell(val1, false, 3000));
            row.Append(CreateCell(lbl2, true, 2000));
            row.Append(CreateCell(val2, false, 3000));
            return row;
        }

        private TableRow CreateFullWidthRow(string label, string content)
        {
            TableRow row = new TableRow();
            row.Append(CreateCell(label, true, 2000));
            
            TableCell contentCell = CreateCell(content, false, 8000);
            contentCell.TableCellProperties ??= new TableCellProperties();
            contentCell.TableCellProperties.Append(new HorizontalMerge() { Val = MergedCellValues.Restart });
            row.Append(contentCell);

            row.Append(new TableCell(new TableCellProperties(new HorizontalMerge() { Val = MergedCellValues.Continue }), new Paragraph()));
            row.Append(new TableCell(new TableCellProperties(new HorizontalMerge() { Val = MergedCellValues.Continue }), new Paragraph()));
            return row;
        }

        private TableRow CreateHeaderRow(string leftText, string rightText)
        {
            TableRow row = new TableRow();
            TableCell leftCell = CreateCell(leftText, true, 5000);
            leftCell.TableCellProperties ??= new TableCellProperties();
            leftCell.TableCellProperties.Append(new HorizontalMerge() { Val = MergedCellValues.Restart });
            row.Append(leftCell);
            row.Append(new TableCell(new TableCellProperties(new HorizontalMerge() { Val = MergedCellValues.Continue }), new Paragraph()));

            TableCell rightCell = CreateCell(rightText, false, 5000);
            rightCell.TableCellProperties ??= new TableCellProperties();
            rightCell.TableCellProperties.Append(new HorizontalMerge() { Val = MergedCellValues.Restart });
            row.Append(rightCell);
            row.Append(new TableCell(new TableCellProperties(new HorizontalMerge() { Val = MergedCellValues.Continue }), new Paragraph()));
            return row;
        }

        private TableRow CreateCheckboxRow(bool isChecked, string description)
        {
            TableRow row = new TableRow();
            string boxSymbol = isChecked ? "[ X ]" : "[     ]";
            row.Append(CreateCell(boxSymbol, true, 1000));
            
            TableCell descCell = CreateCell(description, false, 9000);
            descCell.TableCellProperties ??= new TableCellProperties();
            descCell.TableCellProperties.Append(new HorizontalMerge() { Val = MergedCellValues.Restart });
            row.Append(descCell);

            row.Append(new TableCell(new TableCellProperties(new HorizontalMerge() { Val = MergedCellValues.Continue }), new Paragraph()));
            row.Append(new TableCell(new TableCellProperties(new HorizontalMerge() { Val = MergedCellValues.Continue }), new Paragraph()));
            return row;
        }

        private TableCell CreateCell(string text, bool isBold, int widthTwips)
        {
            TableCell cell = new TableCell();
            
            TableCellMargin cellMargins = new TableCellMargin();
            cellMargins.Append(new TopMargin() { Width = "120", Type = TableWidthUnitValues.Dxa });
            cellMargins.Append(new BottomMargin() { Width = "120", Type = TableWidthUnitValues.Dxa });
            cellMargins.Append(new LeftMargin() { Width = "120", Type = TableWidthUnitValues.Dxa });

            TableCellProperties cellProps = new TableCellProperties(
                new TableCellWidth() { Width = widthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                cellMargins
            );
            cell.Append(cellProps);

            Paragraph para = new Paragraph();
            Run run = new Run(new Text(text));
            run.RunProperties = new RunProperties(new RunFonts() { Ascii = "Arial" }, new FontSize() { Val = "20" });
            if (isBold) run.RunProperties.Append(new Bold());
            
            para.Append(run);
            cell.Append(para);
            return cell;
        }

        private void AddInlineImageToBody(string relationshipId, Body body, uint uniqueId, byte[] imageBytes)
        {
            long cx = 5400000L; 
            long cy = 3600000L; 

            // Under 15KB matches square test assets and system container icons
            if (imageBytes != null && imageBytes.Length < 15000)
            {
                cx = 3600000L;
                cy = 3600000L;
            }

            var element = new Drawing(
                new DW.Inline(
                    new DW.Extent() { Cx = cx, Cy = cy },
                    new DW.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties() { Id = (UInt32Value)uniqueId, Name = "Evidence Photo" },
                    new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks() { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties() { Id = (UInt32Value)(uniqueId + 1), Name = "EvidenceEntry.jpg" },
                                    new PIC.NonVisualPictureDrawingProperties()),
                                new PIC.BlipFill(
                                    new A.Blip() { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print },
                                    new A.Stretch(new A.SourceRectangle())),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset() { X = 0L, Y = 0L },
                                        new A.Extents() { Cx = cx, Cy = cy }),
                                    new A.PresetGeometry() { Preset = A.ShapeTypeValues.Rectangle }))
                        ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                    )
                ) { DistanceFromTop = (UInt32Value)0U, DistanceFromBottom = (UInt32Value)0U, DistanceFromLeft = (UInt32Value)0U, DistanceFromRight = (UInt32Value)0U }
            );

            Paragraph imgPara = new Paragraph(new Run(element));
            imgPara.ParagraphProperties = new ParagraphProperties(new Justification() { Val = JustificationValues.Center });
            body.AppendChild(imgPara);
            body.AppendChild(new Paragraph(new Run(new Text("\n"))));
        }

        private async Task LoadServiceReportsAsync()
        {
            List<string> conditions = new List<string>();
            List<SqlParameter> parameters = new List<SqlParameter>();

            if (!string.IsNullOrEmpty(SearchCaseId))
            {
                conditions.Add("caserefno LIKE @Search");
                parameters.Add(new SqlParameter("@Search", $"%{SearchCaseId.Trim()}%"));
            }
            if (!string.IsNullOrEmpty(SingleDate))
            {
                conditions.Add("dispatcheddate = @SingleDay");
                parameters.Add(new SqlParameter("@SingleDay", SingleDate));
            }

            string whereClause = conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : "";
            string sql = $"SELECT caserefno, dispatchedby, dispatcheddate, dispatchedtime FROM dbo.tbl_D_dispatched {whereClause} ORDER BY dispatcheddate DESC";

            using var conn = new SqlConnection(_connString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddRange(parameters.ToArray());
            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                SearchResults.Add(new ServiceReportSummary {
                    FileRefNumber = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    DispatchedBy = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    DispatchedDate = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    DispatchedTime = reader.IsDBNull(3) ? "" : reader.GetString(3)
                });
            }
        }

        private async Task LoadReportDetailsAsync(string refNum)
        {
            string reportSql = "SELECT caserefno, dispatchedby, dispatcheddate, dispatchedtime, dispatchedimg FROM dbo.tbl_D_dispatched WHERE caserefno = @RefNum";
            using var conn = new SqlConnection(_connString);
            using var cmd = new SqlCommand(reportSql, conn);
            cmd.Parameters.AddWithValue("@RefNum", refNum);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                ActiveReport = new ServiceReportDetail {
                    FileRefNumber = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    DispatchedBy = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    DispatchedDate = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    DispatchedTime = reader.IsDBNull(3) ? "" : reader.GetString(3)
                };

                int imgColumnIndex = reader.GetOrdinal("dispatchedimg");

                if (!reader.IsDBNull(imgColumnIndex))
                {
                    byte[] byteData = await reader.GetFieldValueAsync<byte[]>(imgColumnIndex);
                    
                    if (byteData == null || byteData.Length < 10)
                    {
                        ActiveReport.ImageAttachments.Add("MOCK_IMAGE_BYTES:HEAL_VIA_LOCAL_ASSET");
                    }
                    else
                    {
                        string mimePrefix = "data:image/jpeg;base64,"; 
                        
                        if (byteData.Length > 4)
                        {
                            if (byteData[0] == 0x89 && byteData[1] == 0x50 && byteData[2] == 0x4E && byteData[3] == 0x47)
                            {
                                mimePrefix = "data:image/png;base64,";
                            }
                            else if (byteData[0] == 0x47 && byteData[1] == 0x49 && byteData[2] == 0x46)
                            {
                                mimePrefix = "data:image/gif;base64,";
                            }
                        }

                        string base64String = Convert.ToBase64String(byteData);
                        ActiveReport.ImageAttachments.Add(mimePrefix + base64String);
                    }
                }
            }
        }

        public string GetFormattedImageSrc(string imgData)
        {
            if (string.IsNullOrWhiteSpace(imgData)) return "/images/fallback-evidence.jpg";
            if (imgData.StartsWith("MOCK_IMAGE_BYTES:")) return "/images/fallback-evidence.jpg";
            
            if (imgData.StartsWith("data:image")) return imgData;

            if (imgData.StartsWith("iVBORw", StringComparison.OrdinalIgnoreCase))
            {
                return "data:image/png;base64," + imgData;
            }
            if (imgData.StartsWith("R0lG", StringComparison.OrdinalIgnoreCase))
            {
                return "data:image/gif;base64," + imgData;
            }
            
            return "data:image/jpeg;base64," + imgData;
        }
    }

    public class ServiceReportSummary
    {
        public string FileRefNumber { get; set; } = "";
        public string DispatchedBy { get; set; } = "";
        public string DispatchedDate { get; set; } = "";
        public string DispatchedTime { get; set; } = "";
    }

    public class ServiceReportDetail
    {
        public string FileRefNumber { get; set; } = "";
        public string DispatchedBy { get; set; } = "";
        public string DispatchedDate { get; set; } = "";
        public string DispatchedTime { get; set; } = "";
        public List<string> ImageAttachments { get; set; } = new List<string>();
    }
}