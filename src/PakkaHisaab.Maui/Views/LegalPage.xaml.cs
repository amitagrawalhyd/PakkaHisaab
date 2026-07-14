namespace PakkaHisaab.Maui.Views;

[QueryProperty(nameof(Doc), "doc")]
public partial class LegalPage : ContentPage
{
    public string? Doc { get; set; }

    public LegalPage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        bool isTerms = Doc == "terms";
        Title = isTerms ? "Terms of Service" : "Privacy Policy";
        titleLabel.Text = Title;
        bodyLabel.Text = isTerms ? TermsText : PrivacyText;
    }

    const string PrivacyText = @"Effective Date: 14 July 2026

PakkaHisaab (""the App"") is developed and operated by Amit Kumar Agrawal (""we"", ""us"", ""our""). This Privacy Policy explains how we handle information when you use the App.

1. Information We Collect
- Household helper details you enter: name, category, WhatsApp number, and UPI ID.
- Attendance, wage, advance, and settlement records you create for your helpers.
- Basic device and usage diagnostics (crash logs, app performance) via Microsoft App Center, used only to fix bugs and improve stability.
- Voice audio captured only when you tap the microphone button, processed on-device to log an attendance/ledger entry, and not stored or transmitted after processing.

2. How We Use Information
Data you enter is used solely to run the App's core features - tracking attendance, computing wages, and recording payments for the helpers you manage. In Demo mode, all data stays on your device and nothing is synced.

3. Data Storage and Sharing
Your data is stored locally on your device (SQLite). If you sign in with an account (non-Demo mode), your data syncs to our servers solely so you can access it across your own devices. We do not sell your data or share it with third parties for advertising. Payments are initiated through your own installed UPI apps (Google Pay, PhonePe, Paytm, etc.) - the App never processes or stores your bank credentials.

4. Data Retention and Deletion
You can delete any helper's record at any time from within the App. You can permanently delete your account and all associated data from Settings > Delete My Account & Data.

5. Security
We take reasonable technical measures to protect your data, including local storage and secure transmission (HTTPS) for synced accounts.

6. Children's Privacy
The App is not directed at children and is not intended for use by anyone under 18.

7. Changes to This Policy
We may update this policy from time to time. Continued use of the App after changes means you accept the updated policy.

8. Contact Us
For any privacy questions or requests, contact Amit Kumar Agrawal at amit.agrawal.hyd@outlook.com.";

    const string TermsText = @"Effective Date: 14 July 2026

These Terms of Service (""Terms"") govern your use of PakkaHisaab (the ""App""), developed and operated by Amit Kumar Agrawal (""we"", ""us"", ""our""). By using the App, you agree to these Terms.

1. Use of the App
PakkaHisaab helps you track attendance, wages, advances, and payments for household helpers (such as house help, milkmen, drivers, etc.) that you manage. You are responsible for the accuracy of the data you enter.

2. Your Account
You are responsible for maintaining the confidentiality of your login credentials and for all activity under your account. Demo mode does not require an account and keeps all data on your device only.

3. Payments
The App does not process payments itself. ""Pay via UPI"" hands off to UPI apps already installed on your device; any payment made is between you and your chosen payment provider and the recipient. We are not responsible for failed, delayed, or disputed transactions.

4. Acceptable Use
You agree not to use the App for any unlawful purpose or in a way that could harm, disable, or impair the App or interfere with any other party's use of it.

5. No Warranty
The App is provided ""as is"" without warranties of any kind, express or implied. We do not guarantee the App will be error-free, uninterrupted, or fit for a particular purpose.

6. Limitation of Liability
To the maximum extent permitted by law, Amit Kumar Agrawal shall not be liable for any indirect, incidental, or consequential damages arising from your use of the App, including any loss of data or financial loss arising from payments made using third-party UPI apps.

7. Termination
You may stop using the App and delete your account at any time via Settings > Delete My Account & Data. We may suspend or terminate access for misuse of the App.

8. Governing Law
These Terms are governed by the laws of India, without regard to conflict-of-law principles.

9. Changes to These Terms
We may update these Terms from time to time. Continued use of the App after changes means you accept the updated Terms.

10. Contact Us
For any questions about these Terms, contact Amit Kumar Agrawal at amit.agrawal.hyd@outlook.com.";
}
