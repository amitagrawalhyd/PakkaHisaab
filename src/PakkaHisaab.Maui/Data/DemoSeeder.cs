using PakkaHisaab.Maui.Models;
using PakkaHisaab.Shared.Enums;
using SQLite;

namespace PakkaHisaab.Maui.Data;

/// <summary>
/// Seeds the isolated reviewer database (Demo_PakkaHisaab.db) with realistic data:
///   • Geeta — House Help, ₹5,000/month, 2 absences this month, ₹500 advance outstanding
///   • Raju  — Milkman, 1.5 L delivered daily at ₹60/L
/// Runs instantly and entirely offline; backend auth is bypassed and background sync suspended.
/// </summary>
public static class DemoSeeder
{
    public static async Task SeedAsync(SQLiteAsyncConnection db)
    {
        if (await db.Table<LocalHelper>().CountAsync() > 0)
            return; // already seeded

        var now = DateTime.UtcNow;
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        string period = $"{today:yyyy-MM}";

        var geeta = new LocalHelper
        {
            Id = Guid.NewGuid(), Name = "Geeta", WhatsAppNumber = "+919800000001",
            UpiId = "geeta@upi", Category = HelperCategory.HouseHelp,
            WageType = WageType.MonthlySalary, MonthlyWage = 5000m,
            MonthlyAllowedAbsences = 2, CarryOverLeaveAllowed = true,
            ModifiedAtUtc = now, IsDirty = false
        };

        var raju = new LocalHelper
        {
            Id = Guid.NewGuid(), Name = "Raju", WhatsAppNumber = "+919800000002",
            UpiId = "raju.milk@upi", Category = HelperCategory.MilkMan,
            WageType = WageType.PerUnitDelivery, RatePerUnit = 60m, UnitLabel = "L",
            ModifiedAtUtc = now, IsDirty = false
        };

        await db.InsertAllAsync(new[] { geeta, raju });

        var attendance = new List<LocalAttendance>();

        // Geeta: present daily except two absences (5th & 18th, clamped to the past).
        for (var d = monthStart; d <= today; d = d.AddDays(1))
        {
            var status = d.Day is 5 or 18 ? AttendanceStatus.Absent : AttendanceStatus.Present;
            attendance.Add(new LocalAttendance
            {
                Id = Guid.NewGuid(), HelperId = geeta.Id, Date = d.ToString("yyyy-MM-dd"),
                Status = status, ModifiedAtUtc = now, IsDirty = false
            });
        }

        // Raju: 1.5 litres delivered every day so far this month.
        for (var d = monthStart; d <= today; d = d.AddDays(1))
        {
            attendance.Add(new LocalAttendance
            {
                Id = Guid.NewGuid(), HelperId = raju.Id, Date = d.ToString("yyyy-MM-dd"),
                Status = AttendanceStatus.Present, UnitsDelivered = 1.5m,
                ModifiedAtUtc = now, IsDirty = false
            });
        }

        await db.InsertAllAsync(attendance);

        await db.InsertAsync(new LocalLedgerEntry
        {
            Id = Guid.NewGuid(), HelperId = geeta.Id, Type = LedgerEntryType.Advance,
            Amount = 500m, Method = PaymentMethod.Cash, Note = "Festival advance",
            Period = period, OccurredAtUtc = now.AddDays(-6), ModifiedAtUtc = now, IsDirty = false
        });
    }
}
