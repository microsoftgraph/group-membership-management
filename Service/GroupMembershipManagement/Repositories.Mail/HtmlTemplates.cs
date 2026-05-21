// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Mail
{
    public static class HtmlTemplates
    {
        /// <summary>
        /// Styled HTML email template for all GMM notifications.
        /// Tokens: {0}=title text, {1}=subject/alert line, {2}=body HTML, {3}=CTA block (button or empty), {4}=sent date
        /// </summary>
        public const string FallbackTemplate = @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>{1}</title>
</head>
<body style=""margin:0;padding:0;background-color:#f3f2f1;font-family:'Segoe UI',Helvetica,Arial,sans-serif;-webkit-font-smoothing:antialiased;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""background:#f3f2f1;"">
    <tr><td align=""center"" style=""padding:32px 16px;"">

      <table width=""600"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""background:#ffffff;border-radius:2px;overflow:hidden;box-shadow:0 1.6px 3.6px rgba(0,0,0,0.13),0 0.3px 0.9px rgba(0,0,0,0.1);"">

        <!-- Header bar -->
        <tr>
          <td style=""background:#0078d4;padding:14px 24px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:10px;vertical-align:middle;"">
                <span style=""display:inline-block;width:24px;height:24px;line-height:24px;text-align:center;background:#ffffff;color:#0078d4;border-radius:12px;font-size:14px;font-weight:bold;"">&#x2713;</span>
              </td>
              <td style=""vertical-align:middle;"">
                <span style=""color:#ffffff;font-size:16px;font-weight:600;"">Membership Management</span>
              </td>
            </tr></table>
          </td>
        </tr>

        <!-- Title -->
        <tr>
          <td style=""padding:24px 24px 8px;"">
            <span style=""font-size:18px;font-weight:600;color:#323130;"">{0}</span>
          </td>
        </tr>

        <!-- Alert / subject line -->
        <tr>
          <td style=""padding:8px 24px 16px;"">
            <span style=""font-size:14px;color:#a4262c;line-height:1.5;"">{1}</span>
          </td>
        </tr>

        <!-- Divider -->
        <tr><td style=""padding:0 24px;""><hr style=""border:none;border-top:1px solid #edebe9;margin:0;""></td></tr>

        <!-- Message body -->
        <tr>
          <td style=""padding:16px 24px;font-size:14px;line-height:1.65;color:#323130;"">
            {2}
          </td>
        </tr>

        <!-- CTA Button (conditionally included) -->
        {3}

        <!-- Footer -->
        <tr>
          <td style=""padding:12px 24px;background:#faf9f8;border-top:1px solid #edebe9;font-size:11px;color:#a19f9d;"">
            Sent on {4} &middot; Group Membership Management &middot; Microsoft
          </td>
        </tr>

      </table>
    </td></tr>
  </table>
</body>
</html>";

        /// <summary>
        /// CTA button block to inject when a job URL is available.
        /// Token: {0}=job URL
        /// </summary>
        public const string CtaButtonBlock = @"<tr>
          <td style=""padding:8px 24px 28px;"">
            <a href=""{0}"" style=""display:inline-block;padding:8px 20px;background:#0078d4;color:#ffffff;text-decoration:none;border-radius:2px;font-size:14px;font-weight:600;font-family:'Segoe UI',sans-serif;"">
              Go to GMM UI
            </a>
          </td>
        </tr>";


        // ── Styling constants ──────────────────────────────────────────────────────
        private const string BlueBadgeStyle =
            "display:inline-block;background:#e8f4fd;border:1px solid #c7e0f4;color:#0078d4;font-size:11px;font-weight:700;padding:3px 10px;border-radius:999px;letter-spacing:0.5px;";
        private const string OrangePillBadgeStyle =
            "display:inline-block;background:#FFE8D6;border:1px solid #F4C9A6;color:#C75300;font-size:11px;font-weight:700;padding:3px 10px;border-radius:999px;letter-spacing:0.5px;";
        private const string OrangeCalloutTableStyle =
            "border-left:5px solid #603900;background:#fff4ce;border-radius:0 4px 4px 0;";
        private const string GrayCalloutTableStyle =
            "border-left:5px solid #8a8886;background:#f3f2f1;border-radius:0 4px 4px 0;";

        // Outlook-safe bgcolor fallbacks for the callout backgrounds and the colored left border bar.
        private const string OrangeCalloutBgColor = "#fff4ce";
        private const string OrangeCalloutBorderColor = "#603900";
        private const string GrayCalloutBgColor = "#F5F5F5";
        private const string GrayCalloutBorderColor = "#8A8A8A";

        private const string CtaButtonBgColor = "#0078d4";
        private const string OrangeCalloutTitleStyle =
            "font-size:13px;font-weight:700;color:#603900;letter-spacing:0.5px;margin-bottom:8px;";
        private const string GrayCalloutTitleStyle =
            "font-size:13px;font-weight:700;color:#323130;letter-spacing:0.5px;margin-bottom:8px;";

        // ── Per-template header HTML (unique icon + background + title text per type) ──
        private const string SyncStartedHeaderHtml = @"
        <!-- Header bar -->
        <tr>
          <td bgcolor=""#0078d4"" style=""background:#0078d4;padding:14px 24px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:12px;vertical-align:middle;"">
                <img src=""data:image/png;base64,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"" width=""36"" height=""36"" alt=""Sync icon"" style=""display:block;border:0;"" />
              </td>
              <td style=""vertical-align:middle;"">
                <span style=""color:#ffffff;font-size:16px;font-weight:600;"">Initial sync started</span><br />
                <span style=""color:#ffffffcc;font-size:13px;"">Group Membership Management</span>
              </td>
            </tr></table>
          </td>
        </tr>";

        private const string SyncCompletedHeaderHtml = @"
        <!-- Header bar -->
        <tr>
          <td bgcolor=""#0078d4"" style=""background:#0078d4;padding:14px 24px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:12px;vertical-align:middle;"">
                <img src=""data:image/png;base64,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"" width=""36"" height=""36"" alt=""Complete icon"" style=""display:block;border:0;"" />
              </td>
              <td style=""vertical-align:middle;"">
                <span style=""color:#ffffff;font-size:16px;font-weight:600;"">Initial sync completed</span><br />
                <span style=""color:#ffffffcc;font-size:13px;"">Group Membership Management</span>
              </td>
            </tr></table>
          </td>
        </tr>";

        private const string SyncDisabledHeaderHtml = @"
        <!-- Header bar - Soft amber (Action required) matching reference design -->
        <tr>
          <td bgcolor=""#FBE9C0"" style=""background:#FBE9C0;padding:14px 24px;border-bottom:1px solid #F2D9A8;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:12px;vertical-align:middle;"">
                <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
                  <td width=""36"" height=""36"" align=""center"" valign=""middle"" bgcolor=""#4A3100"" style=""background:#4A3100;width:36px;height:36px;border-radius:50%;line-height:0;font-size:0;mso-line-height-rule:exactly;"">
                    <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
                      <td width=""4"" height=""14"" bgcolor=""#FFFFFF"" style=""background:#FFFFFF;width:4px;height:14px;border-radius:1px;line-height:0;font-size:0;"">&nbsp;</td>
                      <td width=""4"" style=""width:4px;line-height:0;font-size:0;"">&nbsp;</td>
                      <td width=""4"" height=""14"" bgcolor=""#FFFFFF"" style=""background:#FFFFFF;width:4px;height:14px;border-radius:1px;line-height:0;font-size:0;"">&nbsp;</td>
                    </tr></table>
                  </td>
                </tr></table>
              </td>
              <td style=""vertical-align:middle;"">
                <span style=""color:#6B4500;font-size:15px;font-weight:600;"">Sync paused &mdash; {1}</span><br />
                <span style=""color:#4A3100;opacity:0.75;font-size:12.5px;"">Group Membership Management</span>
              </td>
            </tr></table>
          </td>
        </tr>";

        private const string SubmissionRejectedHeaderHtml = @"
        <!-- Header bar - Light amber to match callout palette -->
        <tr>
          <td style=""background:#fff4ce;padding:14px 24px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:12px;vertical-align:middle;"">
                <span style=""display:inline-block;width:36px;height:36px;line-height:36px;text-align:center;background:#603900;color:#ffffff;border-radius:50%;font-size:20px;font-weight:700;"">&#x2715;</span>
              </td>
              <td style=""vertical-align:middle;"">
                <span style=""color:#603900;font-size:16px;font-weight:600;"">Submission Rejected</span><br />
                <span style=""color:#603900cc;font-size:13px;"">Group Membership Management</span>
              </td>
            </tr></table>
          </td>
        </tr>";

        private const string JobPurgingWarningHeaderHtml = @"
        <!-- Header bar - Light amber to match callout palette -->
        <tr>
          <td style=""background:#fff4ce;padding:14px 24px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:12px;vertical-align:middle;"">
                <span style=""display:inline-block;width:36px;height:36px;line-height:36px;text-align:center;background:#603900;color:#ffffff;border-radius:50%;font-size:22px;font-weight:700;"">&#x26A0;</span>
              </td>
              <td style=""vertical-align:middle;"">
                <span style=""color:#603900;font-size:16px;font-weight:600;"">{1}</span><br />
                <span style=""color:#603900cc;font-size:13px;"">Group Membership Management</span>
              </td>
            </tr></table>
          </td>
        </tr>";

        // ── Public template properties ─────────────────────────────────────────────

        /// <summary>Body-only fragment. Tokens: {0}=badge, {1}=headerText (only rendered by SyncDisabled header),
        /// {2}=title, {3}=description, {4}=detailsRows, {5}=calloutTitle, {6}=calloutBody,
        /// {7}=ctaLabel, {8}=ctaUrl, {9}=footerExplanation, {10}=sentDate</summary>
        public static string SyncStartedTemplate =>
            BuildEmailBodyTemplate(SyncStartedHeaderHtml, BlueBadgeStyle, GrayCalloutTableStyle, GrayCalloutTitleStyle, GrayCalloutBgColor, GrayCalloutBorderColor);

        /// <summary>Body-only fragment. Same tokens as SyncStartedTemplate.</summary>
        public static string SyncCompletedTemplate =>
            BuildEmailBodyTemplate(SyncCompletedHeaderHtml, BlueBadgeStyle, GrayCalloutTableStyle, GrayCalloutTitleStyle, GrayCalloutBgColor, GrayCalloutBorderColor);

        /// <summary>Body-only fragment. Same tokens as SyncStartedTemplate. Token {1}=headerText is rendered
        /// in the header bar (e.g. "Sync paused - {1}") with the localized disable reason.</summary>
        public static string SyncDisabledTemplate =>
            BuildEmailBodyTemplate(SyncDisabledHeaderHtml, OrangePillBadgeStyle, GrayCalloutTableStyle, GrayCalloutTitleStyle, GrayCalloutBgColor, GrayCalloutBorderColor);

        /// <summary>Body-only fragment. Same tokens as SyncStartedTemplate.</summary>
        public static string SubmissionRejectedTemplate =>
            BuildEmailBodyTemplate(SubmissionRejectedHeaderHtml, OrangePillBadgeStyle, OrangeCalloutTableStyle, OrangeCalloutTitleStyle);

        /// <summary>Body-only fragment. Same tokens as SyncStartedTemplate. Token {1}=headerText is rendered
        /// in the header bar with a localized title (e.g. "Sync job will be purged soon").</summary>
        public static string JobPurgingWarningTemplate =>
            BuildEmailBodyTemplate(JobPurgingWarningHeaderHtml, OrangePillBadgeStyle, OrangeCalloutTableStyle, OrangeCalloutTitleStyle);

        // ── Shared HTML body builder ───────────────────────────────────────────────
        private static string BuildEmailBodyTemplate(
            string headerHtml,
            string badgeStyle,
            string calloutTableStyle,
            string calloutTitleStyle) =>
            BuildEmailBodyTemplate(
                headerHtml,
                badgeStyle,
                calloutTableStyle,
                calloutTitleStyle,
                OrangeCalloutBgColor,
                OrangeCalloutBorderColor);

        private static string BuildEmailBodyTemplate(
            string headerHtml,
            string badgeStyle,
            string calloutTableStyle,
            string calloutTitleStyle,
            string calloutBgColor,
            string calloutBorderColor) =>
            $@"<table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""background:#f3f2f1;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">
    <tr><td align=""center"" style=""padding:32px 16px;"">

      <table width=""600"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""background:#ffffff;border-radius:4px;overflow:hidden;box-shadow:0 2px 4px rgba(0,0,0,0.1);"">
{headerHtml}

        <!-- Badge -->
        <tr>
          <td style=""padding:24px 24px 4px;"">
            <span style=""{badgeStyle}"">{{0}}</span>
          </td>
        </tr>

        <!-- Title -->
        <tr>
          <td style=""padding:8px 24px 6px;"">
            <span style=""font-size:22px;font-weight:600;color:#242424;"">{{2}}</span>
          </td>
        </tr>

        <!-- Description -->
        <tr>
          <td style=""padding:4px 24px 20px;font-size:14px;line-height:1.6;color:#424242;"">
            {{3}}
          </td>
        </tr>

        <!-- Optional Action Checklist (orange callout) -->
        {{11}}

        <!-- Details Table -->
        <tr>
          <td style=""padding:0 24px 20px;"">
            <div style=""border:1px solid #E1DFDD;border-radius:4px;overflow:hidden;background:#FFFFFF;"">
              <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;"">
                {{4}}
              </table>
            </div>
          </td>
        </tr>

        <!-- Callout Box: two-cell layout so Outlook keeps the colored left bar + fill -->
        <tr>
          <td style=""padding:0 24px 24px;"">
            <div style=""border:1px solid #E0E0E0;border-radius:4px;overflow:hidden;"">
              <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;"">
                <tr>
                  <td width=""3"" bgcolor=""{calloutBorderColor}"" style=""background:{calloutBorderColor};width:3px;line-height:0;font-size:0;"">&nbsp;</td>
                  <td bgcolor=""{calloutBgColor}"" style=""background:{calloutBgColor};padding:14px 16px;"">
                    <div style=""{calloutTitleStyle}"">{{5}}</div>
                    <div style=""font-size:13.5px;line-height:1.5;color:#605E5C;"">{{6}}</div>
                  </td>
                </tr>
              </table>
            </div>
          </td>
        </tr>

        <!-- CTA Button -->
        <tr>
          <td style=""padding:0 24px 28px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;"">
              <tr>
                <td align=""center"" bgcolor=""{CtaButtonBgColor}"" style=""background:{CtaButtonBgColor};border-radius:4px;"">
                  <a href=""{{8}}"" style=""display:inline-block;padding:10px 24px;background:{CtaButtonBgColor};color:#ffffff;text-decoration:none;border-radius:4px;font-size:14px;font-weight:600;font-family:'Segoe UI',sans-serif;mso-padding-alt:0;"">
                    <span style=""color:#ffffff;"">{{7}}</span>
                  </a>
                </td>
              </tr>
            </table>
          </td>
        </tr>

        <!-- Divider -->
        <tr><td style=""padding:0 24px;""><hr style=""border:none;border-top:1px solid #edebe9;margin:0;""></td></tr>

        <!-- Footer explanation -->
        <tr>
          <td style=""padding:16px 24px 8px;font-size:13px;line-height:1.5;color:#605e5c;"">
            {{9}}
          </td>
        </tr>

        <!-- Sent date footer -->
        <tr>
          <td style=""padding:8px 24px 20px;font-size:12px;color:#a19f9d;"">
            Sent {{10}} &middot; Group Membership Management &middot; Microsoft
          </td>
        </tr>

      </table>
    </td></tr>
  </table>";

        /// <summary>
        /// A single row for the details table in the SyncStarted/SyncCompleted templates.
        /// Tokens: {0}=label, {1}=value, {2}=optional style override for value cell
        /// </summary>
        // ── Action checklist (orange "What to do" callout) ─────────────────────────
        // Email-safe orange-tinted callout placed between description and details table.
        // Tokens: {0}=title, {1}=deadline span (already-formatted HTML or empty),
        //         {2}=body HTML.
        public const string OrangeActionChecklistHtml = @"
        <tr>
          <td style=""padding:0 24px 16px;"">
            <div style=""border:1px solid #F2D9A8;border-radius:4px;overflow:hidden;"">
              <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;"">
                <tr>
                  <td width=""3"" bgcolor=""#C75300"" style=""background:#C75300;width:3px;line-height:0;font-size:0;"">&nbsp;</td>
                  <td bgcolor=""#FFF8F0"" style=""background:#FFF8F0;padding:14px 16px;"">
                    <div style=""font-size:13px;font-weight:700;color:#6B4500;letter-spacing:0.3px;margin-bottom:6px;"">{0}<span style=""font-weight:400;color:#8A6D3B;letter-spacing:0;""> {1}</span></div>
                    <div style=""font-size:13.5px;line-height:1.55;color:#4A3100;"">{2}</div>
                  </td>
                </tr>
              </table>
            </div>
          </td>
        </tr>";

        public const string DetailsTableRow= @"<tr>
                <td style=""padding:12px 16px;font-size:12px;font-weight:600;color:#605e5c;text-transform:uppercase;letter-spacing:0.3px;border-right:1px solid #E1DFDD;border-bottom:1px solid #E1DFDD;width:180px;vertical-align:top;"">{0}</td>
                <td style=""padding:12px 16px;font-size:13.5px;color:#242424;border-bottom:1px solid #E1DFDD;{2}"">{1}</td>
              </tr>";
    }
}