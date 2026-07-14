using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Shared.Domain;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;
using SkiaSharp;

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

static class Palette
{
    public static readonly SKColor Teal = SKColor.Parse("#0F766E");
    public static readonly SKColor BrightTeal = SKColor.Parse("#14B8A6");
    public static readonly SKColor Emerald = SKColor.Parse("#10B981");
    public static readonly SKColor Slate = SKColor.Parse("#1E293B");
    public static readonly SKColor Muted = SKColor.Parse("#64748B");
    public static readonly SKColor Red = SKColor.Parse("#EF4444");
    public static readonly SKColor Amber = SKColor.Parse("#F59E0B");
    public static readonly SKColor RowBorder = SKColor.Parse("#E2E8F0");
    public static readonly SKColor CardBg = SKColor.Parse("#F0FDFA");
}

/// <summary>
/// Hand-drawn A4 reports rendered directly with SkiaSharp (SKDocument.CreatePdf), which — unlike
/// QuestPDF's bundled renderer — ships real native binaries for Android and iOS. Each report runs
/// a cheap no-canvas "measure" pass first to learn the total page count (needed for the "n / N"
/// footer), then a second pass draws for real now that the total is known.
/// </summary>
public sealed class PdfReportService : IPdfReportService
{
    readonly IDataService _data;
    readonly LocalizationResourceManager _loc = LocalizationResourceManager.Instance;
    SKBitmap? _logo;

    public PdfReportService(IDataService data) => _data = data;

    async Task<SKBitmap?> GetLogoAsync()
    {
        if (_logo is null)
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("report_logo.png");
            _logo = SKBitmap.Decode(stream);
        }
        return _logo;
    }

    public async Task<string> GenerateHelperLedgerAsync(HelperDto helper, int year, int month)
    {
        var attendance = (await _data.GetAttendanceAsync(helper.Id, year, month))
            .OrderBy(a => a.Date).ToList();
        var ledger = (await _data.GetLedgerAsync(helper.Id, $"{year:D4}-{month:D2}"))
            .OrderBy(l => l.OccurredAtUtc).ToList();
        var breakdown = await _data.ComputeSettlementAsync(helper.Id, year, month);
        var logo = await GetLogoAsync();
        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy", _loc.CurrentCulture);

        var path = Path.Combine(FileSystem.CacheDirectory,
            $"Ledger_{Sanitize(helper.Name)}_{year:D4}-{month:D2}.pdf");

        void Compose(PageFlow flow) => ComposeLedger(flow, helper, attendance, ledger, breakdown);

        RenderTwoPass(path, logo, _loc.Get("Report_LedgerTitle"), $"{helper.Name} — {monthName}", Compose);
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

        void Compose(PageFlow flow) => ComposeHousehold(flow, rows);

        RenderTwoPass(path, logo, _loc.Get("Report_HouseholdTitle"), monthName, Compose);
        return path;
    }

    void RenderTwoPass(string path, SKBitmap? logo, string title, string subtitle, Action<PageFlow> compose)
    {
        var generatedBy = $"{_loc.Get("Report_GeneratedBy")} PakkaHisaab ·";

        var measure = new PageFlow(null, logo, title, subtitle, generatedBy, knownTotalPages: null);
        compose(measure);
        var totalPages = measure.PageCount;

        using var stream = new SKFileWStream(path);
        using var doc = SKDocument.CreatePdf(stream);
        var flow = new PageFlow(doc, logo, title, subtitle, generatedBy, totalPages);
        compose(flow);
        flow.Finish();
    }

    void ComposeLedger(PageFlow flow, HelperDto helper, List<AttendanceDto> attendance,
        List<LedgerEntryDto> ledger, SettlementBreakdown breakdown)
    {
        var edges = PageFlow.ColumnEdges(2, 3, 2);
        DrawHeaderRow(flow, edges, new[]
        {
            (_loc.Get("Report_Date"), SKTextAlign.Left),
            (_loc.Get("Report_Status"), SKTextAlign.Left),
            (_loc.Get("Report_Units"), SKTextAlign.Right),
        });

        foreach (var a in attendance)
        {
            var (label, color) = a.Status switch
            {
                AttendanceStatus.Present => (_loc.Get("Status_Present"), Palette.Emerald),
                AttendanceStatus.Absent => (_loc.Get("Status_Absent"), Palette.Red),
                _ => (_loc.Get("Status_HalfDay"), Palette.Amber)
            };
            var units = a.UnitsDelivered > 0 ? $"{a.UnitsDelivered:0.##} {helper.UnitLabel}" : "—";
            DrawBodyRow(flow, edges, new[]
            {
                new Cell(a.Date, null, false, SKTextAlign.Left),
                new Cell(label, color, true, SKTextAlign.Left),
                new Cell(units, null, false, SKTextAlign.Right),
            });
        }

        if (ledger.Count > 0)
        {
            DrawSectionTitle(flow, _loc.Get("Report_Movements"));
            var mEdges = PageFlow.ColumnEdges(3, 3, 2);
            foreach (var l in ledger)
            {
                DrawBodyRow(flow, mEdges, new[]
                {
                    new Cell(l.OccurredAtUtc.ToLocalTime().ToString("dd MMM"), null, false, SKTextAlign.Left),
                    new Cell(_loc.Get($"LedgerType_{l.Type}"), null, false, SKTextAlign.Left),
                    new Cell($"₹ {l.Amount:N2}", null, false, SKTextAlign.Right),
                });
            }
        }

        DrawSettlementCard(flow, breakdown);
    }

    void ComposeHousehold(PageFlow flow, List<(HelperDto Helper, SettlementBreakdown B)> rows)
    {
        var edges = PageFlow.ColumnEdges(3, 2, 2, 2, 2);
        DrawHeaderRow(flow, edges, new[]
        {
            (_loc.Get("Report_Helper"), SKTextAlign.Left),
            (_loc.Get("Report_GrossWage"), SKTextAlign.Right),
            (_loc.Get("Report_Absences"), SKTextAlign.Right),
            (_loc.Get("Report_Advances"), SKTextAlign.Right),
            (_loc.Get("Report_FinalPayable"), SKTextAlign.Right),
        });

        foreach (var (helper, b) in rows)
        {
            DrawBodyRow(flow, edges, new[]
            {
                new Cell(helper.Name, null, true, SKTextAlign.Left),
                new Cell($"₹ {b.GrossWage:N2}", null, false, SKTextAlign.Right),
                new Cell($"{b.AbsentDays + b.HalfDays:0.#}", null, false, SKTextAlign.Right),
                new Cell($"₹ {b.Advances:N2}", null, false, SKTextAlign.Right),
                new Cell($"₹ {b.FinalPayable:N2}", Palette.Teal, true, SKTextAlign.Right),
            });
        }

        DrawBodyRow(flow, edges, new[]
        {
            new Cell("", null, false, SKTextAlign.Left),
            new Cell("", null, false, SKTextAlign.Right),
            new Cell("", null, false, SKTextAlign.Right),
            new Cell(_loc.Get("Report_Total"), null, true, SKTextAlign.Right),
            new Cell($"₹ {rows.Sum(r => r.B.FinalPayable):N2}", Palette.Emerald, true, SKTextAlign.Right),
        });
    }

    // ---------- drawing primitives ----------

    readonly record struct Cell(string Text, SKColor? Color, bool Bold, SKTextAlign Align);

    static void DrawHeaderRow(PageFlow flow, float[] edges, (string Text, SKTextAlign Align)[] cells)
    {
        const float h = 22f;
        flow.EnsureSpace(h);
        var c = flow.Canvas;
        if (c is not null)
        {
            using var bg = new SKPaint { Color = Palette.Teal, Style = SKPaintStyle.Fill };
            c.DrawRect(SKRect.Create(edges[0], flow.Y, edges[^1] - edges[0], h), bg);
            using var text = new SKPaint
            {
                IsAntialias = true, Color = SKColors.White, TextSize = 9.5f, FakeBoldText = true
            };
            for (var i = 0; i < cells.Length; i++)
                DrawCellText(c, text, cells[i].Text, edges[i], edges[i + 1], flow.Y, h, cells[i].Align);
        }
        flow.Advance(h);
    }

    static void DrawBodyRow(PageFlow flow, float[] edges, Cell[] cells)
    {
        const float h = 20f;
        flow.EnsureSpace(h);
        var c = flow.Canvas;
        if (c is not null)
        {
            using var border = new SKPaint { Color = Palette.RowBorder, StrokeWidth = 0.6f };
            c.DrawLine(edges[0], flow.Y + h, edges[^1], flow.Y + h, border);
            for (var i = 0; i < cells.Length; i++)
            {
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    TextSize = 9.5f,
                    Color = cells[i].Color ?? Palette.Slate,
                    FakeBoldText = cells[i].Bold
                };
                DrawCellText(c, paint, cells[i].Text, edges[i], edges[i + 1], flow.Y, h, cells[i].Align);
            }
        }
        flow.Advance(h);
    }

    static void DrawCellText(SKCanvas c, SKPaint paint, string text, float xStart, float xEnd,
        float yTop, float rowHeight, SKTextAlign align, float padding = 6f)
    {
        if (string.IsNullOrEmpty(text)) return;
        paint.TextAlign = align;
        var m = paint.FontMetrics;
        var baseline = yTop + rowHeight / 2f - (m.Ascent + m.Descent) / 2f;
        var x = align switch
        {
            SKTextAlign.Left => xStart + padding,
            SKTextAlign.Right => xEnd - padding,
            _ => (xStart + xEnd) / 2f
        };
        c.DrawText(text, x, baseline, paint);
    }

    static void DrawSectionTitle(PageFlow flow, string text)
    {
        const float h = 24f;
        flow.EnsureSpace(h);
        var c = flow.Canvas;
        if (c is not null)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true, TextSize = 12f, Color = Palette.Teal, FakeBoldText = true
            };
            var m = paint.FontMetrics;
            c.DrawText(text, PageFlow.ContentLeft, flow.Y + h - 8 - m.Descent, paint);
        }
        flow.Advance(h);
    }

    void DrawSettlementCard(PageFlow flow, SettlementBreakdown b)
    {
        var rows = new List<(string Label, string Value)>
        {
            (_loc.Get("Report_GrossWage"), $"₹ {b.GrossWage:N2}"),
            (_loc.Get("Report_UnpaidAbsences"),
                $"{b.UnpaidAbsenceDays:0.#} ({_loc.Get("Report_Deduction")} ₹ {b.AbsenceDeduction:N2})"),
            (_loc.Get("Report_Advances"), $"− ₹ {b.Advances:N2}"),
        };
        if (b.Bonuses > 0) rows.Add((_loc.Get("Report_Bonuses"), $"+ ₹ {b.Bonuses:N2}"));

        const float topGap = 12f, pad = 14f, rowH = 18f, finalRowH = 28f;
        var cardHeight = pad * 2 + rows.Count * rowH + finalRowH;

        flow.Advance(topGap);
        flow.EnsureSpace(cardHeight);
        var c = flow.Canvas;
        var top = flow.Y;
        if (c is not null)
        {
            var rect = SKRect.Create(PageFlow.ContentLeft, top, PageFlow.ContentWidth, cardHeight);
            using (var bg = new SKPaint { Color = Palette.CardBg, Style = SKPaintStyle.Fill, IsAntialias = true })
                c.DrawRoundRect(rect, 4, 4, bg);
            using (var border = new SKPaint
            {
                Color = Palette.BrightTeal, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true
            })
                c.DrawRoundRect(rect, 4, 4, border);

            var y = top + pad;
            using (var label = new SKPaint { IsAntialias = true, TextSize = 9.5f, Color = Palette.Muted })
            using (var value = new SKPaint
            {
                IsAntialias = true, TextSize = 9.5f, Color = Palette.Slate, TextAlign = SKTextAlign.Right
            })
            {
                foreach (var r in rows)
                {
                    c.DrawText(r.Label, PageFlow.ContentLeft + pad, y + 10, label);
                    c.DrawText(r.Value, PageFlow.ContentLeft + PageFlow.ContentWidth - pad, y + 10, value);
                    y += rowH;
                }
            }

            y += 6;
            using var finalLabel = new SKPaint
            {
                IsAntialias = true, TextSize = 12.5f, Color = Palette.Slate, FakeBoldText = true
            };
            using var finalValue = new SKPaint
            {
                IsAntialias = true, TextSize = 14f, Color = Palette.Teal, FakeBoldText = true,
                TextAlign = SKTextAlign.Right
            };
            c.DrawText(_loc.Get("Report_FinalPayable"), PageFlow.ContentLeft + pad, y + 14, finalLabel);
            c.DrawText($"₹ {b.FinalPayable:N2}", PageFlow.ContentLeft + PageFlow.ContentWidth - pad, y + 14, finalValue);
        }
        flow.Advance(cardHeight);
    }

    public Task ShareAsync(string filePath) =>
        Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = _loc.Get("Report_ShareTitle"),
            File = new ShareFile(filePath) // WhatsApp appears in the OS share sheet
        });

    static string Sanitize(string s) =>
        string.Concat(s.Split(Path.GetInvalidFileNameChars()));
}

/// <summary>
/// Tracks the current page/canvas/cursor for one report. Runs in "measure" mode (doc is null — no
/// drawing, just counts pages) or "draw" mode (real SKDocument, real canvas). Both modes execute the
/// exact same compose logic, so the page count from a measure pass is always accurate for the
/// matching draw pass.
/// </summary>
sealed class PageFlow
{
    public const float PageWidth = 595f;
    public const float PageHeight = 842f;
    public const float Margin = 36f;
    const float HeaderHeight = 66f;
    const float FooterReserve = 26f;
    public const float ContentLeft = Margin;
    public const float ContentTop = Margin + HeaderHeight;
    public const float ContentBottom = PageHeight - Margin - FooterReserve;
    public const float ContentWidth = PageWidth - 2 * Margin;

    readonly SKDocument? _doc;
    readonly SKBitmap? _logo;
    readonly string _title;
    readonly string _subtitle;
    readonly string _generatedBy;
    readonly int? _knownTotalPages;

    public int PageCount { get; private set; }
    public SKCanvas? Canvas { get; private set; }
    public float Y { get; private set; }

    public PageFlow(SKDocument? doc, SKBitmap? logo, string title, string subtitle,
        string generatedBy, int? knownTotalPages)
    {
        _doc = doc;
        _logo = logo;
        _title = title;
        _subtitle = subtitle;
        _generatedBy = generatedBy;
        _knownTotalPages = knownTotalPages;
        StartPage();
    }

    /// <summary>Splits the content width into columns by relative ratio (mirrors QuestPDF's RelativeColumn).</summary>
    public static float[] ColumnEdges(params float[] ratios)
    {
        var edges = new float[ratios.Length + 1];
        edges[0] = ContentLeft;
        var total = ratios.Sum();
        var acc = ContentLeft;
        for (var i = 0; i < ratios.Length; i++)
        {
            acc += ContentWidth * (ratios[i] / total);
            edges[i + 1] = acc;
        }
        return edges;
    }

    public void EnsureSpace(float height)
    {
        if (Y + height > ContentBottom)
            StartPage();
    }

    public void Advance(float dy) => Y += dy;

    public void Finish() => _doc?.EndPage();

    void StartPage()
    {
        if (_doc is not null)
        {
            if (PageCount > 0) _doc.EndPage();
            Canvas = _doc.BeginPage(PageWidth, PageHeight);
        }
        PageCount++;
        Y = ContentTop;
        DrawHeader();
        DrawFooter();
    }

    void DrawHeader()
    {
        var c = Canvas;
        if (c is null) return;

        const float logoSize = 46f;
        if (_logo is not null)
            c.DrawBitmap(_logo, SKRect.Create(Margin, Margin, logoSize, logoSize));

        var textX = Margin + logoSize + 12;
        using (var brand = new SKPaint { IsAntialias = true, TextSize = 8.5f, Color = Palette.Muted })
            c.DrawText("PakkaHisaab · ClearKhata", textX, Margin + 11, brand);
        using (var title = new SKPaint { IsAntialias = true, TextSize = 15.5f, Color = Palette.Teal, FakeBoldText = true })
            c.DrawText(_title, textX, Margin + 28, title);
        using (var subtitle = new SKPaint { IsAntialias = true, TextSize = 10.5f, Color = Palette.Slate })
            c.DrawText(_subtitle, textX, Margin + 43, subtitle);
        using (var line = new SKPaint { Color = Palette.RowBorder, StrokeWidth = 1 })
            c.DrawLine(Margin, Margin + logoSize + 8, PageWidth - Margin, Margin + logoSize + 8, line);
    }

    void DrawFooter()
    {
        var c = Canvas;
        if (c is null) return;

        var total = _knownTotalPages?.ToString() ?? "?";
        using var paint = new SKPaint
        {
            IsAntialias = true, TextSize = 8f, Color = Palette.Muted, TextAlign = SKTextAlign.Center
        };
        c.DrawText($"{_generatedBy} {PageCount} / {total}", PageWidth / 2f, PageHeight - Margin + 10, paint);
    }
}
