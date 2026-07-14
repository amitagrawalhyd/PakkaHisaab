namespace PakkaHisaab.Maui.Helpers;

public static class Constants
{
    public const string ApiBaseUrl =
#if DEBUG
        "https://10.0.2.2:7215"; // Android emulator loopback; use https://localhost:7215 on iOS simulator
#else
        // Free-tier default: your Azure F1 web app (set by deploy/azure_free_deploy.sh output).
        "https://api-pakkahisaab.azurewebsites.net";
#endif

    public const string MainDbName = "PakkaHisaab.db";
    public const string DemoDbName = "Demo_PakkaHisaab.db";

    // SecureStorage keys
    public const string KeyAccessToken = "ph_access_token";
    public const string KeyRefreshToken = "ph_refresh_token";
    public const string KeyUserId = "ph_user_id";

    // Preferences keys
    public const string KeyIsDemo = "ph_is_demo";
    public const string KeyLanguage = "ph_language";
    public const string KeyOnboarded = "ph_onboarded";
    public const string KeySyncWatermark = "ph_sync_watermark";
    public const string KeyDeviceId = "ph_device_id";

    // Notification identity ranges (id = base + stable per-helper offset)
    public const int DailyAttendanceNotificationBase = 51_000;
    public const int SalaryAlertNotificationBase = 61_000;
    public const string AttendanceCategory = "ph_attendance";
    public const string ActionMarkAbsent = "ph_action_absent";

    // AppCenter — replace with your real secrets before release builds.
    public const string AppCenterAndroidSecret = "android=YOUR-ANDROID-APPCENTER-SECRET";
    public const string AppCenterIosSecret = "ios=YOUR-IOS-APPCENTER-SECRET";

    public const string PrivacyPolicyUrl = "https://pakkahisaab.app/privacy";
    public const string TermsUrl = "https://pakkahisaab.app/terms";
}

/// <summary>
/// Material Symbols (Rounded) codepoints used across the UI — font glyphs, never raster icons.
/// Codepoints are from the official Material Symbols codepoint map.
/// </summary>
public static class IconFont
{
    public const string Home = "\ue88a";          // home
    public const string CalendarMonth = "\uebcc"; // calendar_month
    public const string Payments = "\uef63";      // payments
    public const string Receipt = "\uef6e";       // receipt_long
    public const string Settings = "\ue8b8";      // settings
    public const string Add = "\ue145";           // add
    public const string Person = "\ue7fd";        // person
    public const string Mic = "\ue029";           // mic
    public const string Share = "\ue80d";         // share
    public const string Delete = "\ue872";        // delete
    public const string CheckCircle = "\ue86c";   // check_circle
    public const string Cancel = "\ue5c9";        // cancel
    public const string Timelapse = "\ue422";     // timelapse (half-day)
    public const string WaterDrop = "\ue798";     // water_drop (milk units)
    public const string Language = "\ue894";      // language
    public const string PictureAsPdf = "\ue415";  // picture_as_pdf
    public const string Chat = "\ue0b7";          // chat (WhatsApp share)
    public const string TrendingUp = "\ue8e5";    // trending_up (forecast)
    public const string ArrowBack = "\ue5c4";     // arrow_back
    public const string Edit = "\ue3c9";          // edit
    public const string Wallet = "\ue850";        // account_balance_wallet
}
