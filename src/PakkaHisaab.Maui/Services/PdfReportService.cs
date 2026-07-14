using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PakkaHisaab.Maui.Services;

public interface IPdfReportService
{
    /// <summary>Report 1 — Helper Monthly Ledger: daily attendance breakdown + money movements.</summary>
    Task<string> GenerateHelperLedgerAsync(HelperDto helper, int year, int month);
    /// <summary>Report 2 — Household Summary: one row per helper with final payables.</summary>
    Task<string> GenerateHouseholdSummaryAsync(int year, int month);
    /// <summary>Hands the PDF to the OS share sheet (WhatsApp first-class target).</summary>
    Task ShareAsync(string filePath);
}

/// <summary>
/// QuestPDF-based localized reports. The brand logo (report_logo.png, packaged as a MauiAsset)
/// is embedded in every header. Colors mirror the app palette.
/// </summary>
public sealed class PdfReportService : IPdfReportService
{
    const string Teal = "#0F766E";
    const string BrightTeal = "#14B8A6";
    const string Emerald = "#10B981";
    const string Slate = "#1E293B";
    const string Muted = "#64748B";

    readonly IDataService _data;
    readonly LocalizationResourceManager _loc = LocalizationResourceManager.Instance;
    byte[]? _logo;

    public PdfReportService(IDataService data)
    {
        _data = data;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    async Task<byte[]> GetLogoAsync()
    {
        if (_logo is null)
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("report_logo.png");
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            _logo = ms.ToArray();
        }
        return _logo;
    }

    public async Task<string> GenerateHelperLedgerAsync(HelperDto helper, int year, int month)
    {
        var attendance = (await _data.GetAttendanceAsync(helper.Id, year, month))
            .OrderBy(a => a.Date).ToList();
        var ledger = await _data.GetLedgerAsync(helper.Id, $"{year:D4}-{month:D2}");
        var breakdown = await _data.ComputeSettlementAsync(helper.Id, year, month);
        var logo = await GetLogoAsync();
        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy", _loc.CurrentCulture);

        var path = Path.Combine(FileSystem.CacheDirectory,
            $"Ledger_{Sanitize(helper.Name)}_{year:D4}-{month:D2}.pdf");

        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Slate));

            page.Header().Element(h => ComposeHeader(h, logo,
                _loc.Get("Report_LedgerTitle"), $"{helper.Name} — {monthName}"));

            page.Content().PaddingVertical(14).Column(col =>
            {
                col.Spacing(14);

                // Daily breakdown table
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Element(HeadCell).Text(_loc.Get("Report_Date"));
                        h.Cell().Element(HeadCell).Text(_loc.Get("Report_Status"));
                        h.Cell().Element(HeadCell).AlignRight().Text(_loc.Get("Report_Units"));
                    });

                    foreach (var a in attendance)
                    {
                        var (label, color) = a.Status switch
                        {
                            AttendanceStatus.Present => (_loc.Get("Status_Present"), Emerald),
                            AttendanceStatus.Absent => (_loc.Get("Status_Absent"), "#EF4444"),
                            _ => (_loc.Get("Status_HalfDay"), "#F59E0B")
                        };
                        table.Cell().Element(BodyCell).Text(a.Date);
                        table.Cell().Element(BodyCell).Text(t => t.Span(label).FontColor(color).SemiBold());
                        table.Cell().Element(BodyCell).AlignRight()
                            .Text(a.UnitsDelivered > 0 ? $"{a.UnitsDelivered:0.##} {helper.UnitLabel}" : "—");
                    }
                });

                // Money movements
                if (ledger.Count > 0)
                {
                    col.Item().Text(_loc.Get("Report_Movements")).FontSize(13).SemiBold().FontColor(Teal);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(3);
                            c.RelativeColumn(2);
                        });
                        foreach (var l in ledger.OrderBy(l => l.OccurredAtUtc))
                        {
                            table.Cell().Element(BodyCell).Text(l.OccurredAtUtc.ToLocalTime().ToString("dd MMM"));
                            table.Cell().Element(BodyCell).Text(_loc.Get($"LedgerType_{l.Type}"));
                            table.Cell().Element(BodyCell).AlignRight().Text($"₹ {l.Amount:N2}");
                        }
                    });
                }

                // Settlement summary card
                col.Item().Background("#F0FDFA").Border(1).BorderColor(BrightTeal)
                    .Padding(14).Column(sc =>
                {
                    sc.Spacing(4);
                    Row(sc, _loc.Get("Report_GrossWage"), $"₹ {breakdown.GrossWage:N2}");
                    Row(sc, _loc.Get("Report_UnpaidAbsences"),
                        $"{breakdown.UnpaidAbsenceDays:0.#} ({_loc.Get("Report_Deduction")} ₹ {breakdown.AbsenceDeduction:N2})");
                    Row(sc, _loc.Get("Report_Advances"), $"− ₹ {breakdown.Advances:N2}");
                    if (breakdown.Bonuses > 0) Row(sc, _loc.Get("Report_Bonuses"), $"+ ₹ {breakdown.Bonuses:N2}");
                    sc.Item().PaddingTop(6).Row(r =>
                    {
                        r.RelativeItem().Text(_loc.Get("Report_FinalPayable")).FontSize(13).Bold();
                        r.ConstantItem(140).AlignRight()
                            .Text($"₹ {breakdown.FinalPayable:N2}").FontSize(15).Bold().FontColor(Teal);
                    });
                });
            });

            page.Footer().Element(ComposeFooter);
        })).GeneratePdf(path);

        return path;
    }

    public async Task<string> GenerateHouseholdSummaryAsync(int year, int month)
    {
        var helpers = await _data.GetHelpersAsync();
        var logo = await GetLogoAsync();
        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy", _loc.CurrentCulture);
        var rows = new List<(HelperDto Helper, SettlementBreakdown B)>();
        foreach (var h in helpers)
            rows.Add((h, await _data.ComputeSettlementAsync(h.Id, year, month)));

        var path = Path.Combine(FileSystem.CacheDirectory, $"Household_{year:D4}-{month:D2}.pdf");

        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Slate));

            page.Header().Element(h => ComposeHeader(h, logo,
                _loc.Get("Report_HouseholdTitle"), monthName));

            page.Content().PaddingVertical(14).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });
                table.Header(h =>
                {
                    h.Cell().Element(HeadCell).Text(_loc.Get("Report_Helper"));
                    h.Cell().Element(HeadCell).AlignRight().Text(_loc.Get("Report_GrossWage"));
                    h.Cell().Element(HeadCell).AlignRight().Text(_loc.Get("Report_Absences"));
                    h.Cell().Element(HeadCell).AlignRight().Text(_loc.Get("Report_Advances"));
                    h.Cell().Element(HeadCell).AlignRight().Text(_loc.Get("Report_FinalPayable"));
                });

                foreach (var (helper, b) in rows)
                {
                    table.Cell().Element(BodyCell).Text(helper.Name).SemiBold();
                    table.Cell().Element(BodyCell).AlignRight().Text($"₹ {b.GrossWage:N2}");
                    table.Cell().Element(BodyCell).AlignRight().Text($"{b.AbsentDays + b.HalfDays:0.#}");
                    table.Cell().Element(BodyCell).AlignRight().Text($"₹ {b.Advances:N2}");
                    table.Cell().Element(BodyCell).AlignRight()
                        .Text($"₹ {b.FinalPayable:N2}").Bold().FontColor(Teal);
                }

                table.Cell().ColumnSpan(4).Element(BodyCell).AlignRight()
                    .Text(_loc.Get("Report_Total")).Bold();
                table.Cell().Element(BodyCell).AlignRight()
                    .Text($"₹ {rows.Sum(r => r.B.FinalPayable):N2}").Bold().FontColor(Emerald);
            });

            page.Footer().Element(ComposeFooter);
        })).GeneratePdf(path);

        return path;
    }

    public Task ShareAsync(string filePath) =>
        Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = _loc.Get("Report_ShareTitle"),
            File = new ShareFile(filePath) // WhatsApp appears in the OS share sheet
        });

    // ---------- layout helpers ----------

    void ComposeHeader(IContainer container, byte[] logo, string title, string subtitle) =>
        container.Row(row =>
        {
            row.ConstantItem(54).Image(logo);
            row.RelativeItem().PaddingLeft(12).Column(col =>
            {
                col.Item().Text("PakkaHisaab · ClearKhata").FontSize(9).FontColor(Muted);
                col.Item().Text(title).FontSize(17).Bold().FontColor(Teal);
                col.Item().Text(subtitle).FontSize(11).FontColor(Slate);
            });
        });

    void ComposeFooter(IContainer container) =>
        container.AlignCenter().Text(t =>
        {
            t.Span($"{_loc.Get("Report_GeneratedBy")} PakkaHisaab · ").FontSize(8).FontColor(Muted);
            t.CurrentPageNumber().FontSize(8).FontColor(Muted);
            t.Span(" / ").FontSize(8).FontColor(Muted);
            t.TotalPages().FontSize(8).FontColor(Muted);
        });

    static IContainer HeadCell(IContainer c) =>
        c.Background("#0F766E").Padding(6).DefaultTextStyle(t => t.FontColor("#FFFFFF").SemiBold());

    static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor("#E2E8F0").Padding(5);

    static void Row(ColumnDescriptor col, string label, string value) =>
        col.Item().Row(r =>
        {
            r.RelativeItem().Text(label).FontColor(Muted);
            r.ConstantItem(170).AlignRight().Text(value);
        });

    static string Sanitize(string s) =>
        string.Concat(s.Split(Path.GetInvalidFileNameChars()));
}
