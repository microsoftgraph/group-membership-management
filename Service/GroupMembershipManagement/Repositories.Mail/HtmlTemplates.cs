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
            "display:inline-block;background:#e8f4fd;color:#0078d4;font-size:11px;font-weight:700;padding:3px 8px;border-radius:3px;letter-spacing:0.5px;";
        private const string OrangePillBadgeStyle =
            "display:inline-block;background:#fff4ce;border:1px solid #e6c89a;color:#603900;font-size:11px;font-weight:700;padding:3px 10px;border-radius:999px;letter-spacing:0.5px;";
        private const string BlueCalloutTableStyle =
            "border-left:5px solid #0078d4;background:#deecf9;border-radius:0 4px 4px 0;";
        private const string OrangeCalloutTableStyle =
            "border-left:5px solid #603900;background:#fff4ce;border-radius:0 4px 4px 0;";
        private const string BlueCalloutTitleStyle =
            "font-size:13px;font-weight:700;color:#004578;letter-spacing:0.5px;margin-bottom:8px;";
        private const string OrangeCalloutTitleStyle =
            "font-size:13px;font-weight:700;color:#603900;letter-spacing:0.5px;margin-bottom:8px;";

        // ── Per-template header HTML (unique icon + background + title text per type) ──
        private const string SyncStartedHeaderHtml = @"
        <!-- Header bar -->
        <tr>
          <td style=""background:#0078d4;padding:14px 24px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:12px;vertical-align:middle;"">
                <img src=""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACQAAAAkCAYAAADhAJiYAAACMklEQVR4Ad3Bv6vVZQDH8ff72ylrSFuUoOiHS1tGbboIhkPwGNFUS2C42BA0N5xrUGtbf0BGXHF7BJckoghSiGgosMEhqqm4ocWlq3w64Blu18P3fJ9zj9dLrxfLkKSwJNIoSWEgtdJIBkhS2Ca1MoDMkaSwJGplDumRpLBkaqWHzJCkcJeplRk6dhnZIklhh6iVLWSTJIUdplY2kakkhXtErUx17DIjtinJQeBpbvtb/RFYY0EykaTQ7jngTeBJ/utWkkvAx+qfDKRWJkYs5gRwEvgL+AT4HlhPcgB4UT2e5NkkY/VXGoxodwg4CVwF3gOuM6VeA74BDqvvJHkXeBvYYKCONgKngBvA+8B1Zvs6yar6OHCCBh0NkuxJsh/4FFijh/oUE0leosF9SQoDqTfV88BP9EjyIHBE/V19JMkl9R96jMfjZ1ZWVq7KRJLCMPcneV69AuwF1lgStTIxos2r6uvcdhY4R48kB4EN9WcG6mjzBRDgFnCRfg8DH6iv0aCjzW9JLgMd8AT93lAfAj6jQUe7j4AbwBg4yp0eAE4Dx5N8DnxLA5lKUhgoyWPAGXU/cA34DlhP8qj6ArAX+BL4ENhgDrUyNWIB6i9J3gJeTnJMfYUJlSQ/qBeAr1jAiAWp68CqugrsA/YAf6g32QbZJElhh6mVTWSLJIUdola2kBmSFO4ytTJDxy4jPZIUlkyt9JA5khSWRK1MIQMkKWyTWhlAGiUpDKRW7oUkhf+rfwHI5NaCqrN6zgAAAABJRU5ErkJggg=="" width=""36"" height=""36"" alt=""Sync icon"" style=""display:block;border:0;"" />
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
          <td style=""background:#0078d4;padding:14px 24px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:12px;vertical-align:middle;"">
                <img src=""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACQAAAAkCAYAAADhAJiYAAABJklEQVR4Ad3BUW7jMBBEwddErjdzWM4BO/kwYK8pS5QtKcFWcQTbwUHETraDSZKKncQE28GHJBUTxAbbwUEkFRvECtvBwSQVK8QC28HJJBULGn+MeGI7uIik4ol4YDu4mKTigbixHfwSScXNF9fp3CUvNK7RmdT4YTs4T2eD7eCmca7OKFnROE9nlGxonKMzSiY0jtcZJZNkO1jXuUvWdUbJJEnVJBXzOq91RskkScWPxn6dUWeUvKGxLRl17jqj5E1fvK+zLPlAY04yJ/mQuLEdbOu8lrxJUnHT2CdZlhyksV/yr+RA4oHt4GKSigfiie3gIpKKJ2KB7eBkkooFjT9GrLAdHExSsUJssB0cRFKxQUywHXxIUjFB7GQ7mCSp+A22g//VN+bDZGbW8fASAAAAAElFTkSuQmCC"" width=""36"" height=""36"" alt=""Complete icon"" style=""display:block;border:0;"" />
              </td>
              <td style=""vertical-align:middle;"">
                <span style=""color:#ffffff;font-size:16px;font-weight:600;"">Onboarding complete</span><br />
                <span style=""color:#ffffffcc;font-size:13px;"">Group Membership Management</span>
              </td>
            </tr></table>
          </td>
        </tr>";

        private const string SyncDisabledHeaderHtml = @"
        <!-- Header bar - Light amber to match callout palette -->
        <tr>
          <td style=""background:#fff4ce;padding:14px 24px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:12px;vertical-align:middle;"">
                <span style=""display:inline-block;width:36px;height:36px;line-height:36px;text-align:center;background:#603900;color:#ffffff;border-radius:50%;font-size:18px;font-weight:700;"">&#x23F8;</span>
              </td>
              <td style=""vertical-align:middle;"">
                <span style=""color:#603900;font-size:16px;font-weight:600;"">Sync paused - {1}</span><br />
                <span style=""color:#603900cc;font-size:13px;"">Group Membership Management</span>
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
            BuildEmailBodyTemplate(SyncStartedHeaderHtml, BlueBadgeStyle, BlueCalloutTableStyle, BlueCalloutTitleStyle);

        /// <summary>Body-only fragment. Same tokens as SyncStartedTemplate.</summary>
        public static string SyncCompletedTemplate =>
            BuildEmailBodyTemplate(SyncCompletedHeaderHtml, BlueBadgeStyle, BlueCalloutTableStyle, BlueCalloutTitleStyle);

        /// <summary>Body-only fragment. Same tokens as SyncStartedTemplate. Token {1}=headerText is rendered
        /// in the header bar (e.g. "Sync paused - {1}") with the localized disable reason.</summary>
        public static string SyncDisabledTemplate =>
            BuildEmailBodyTemplate(SyncDisabledHeaderHtml, OrangePillBadgeStyle, OrangeCalloutTableStyle, OrangeCalloutTitleStyle);

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

        <!-- Details Table -->
        <tr>
          <td style=""padding:0 24px 20px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border:1px solid #edebe9;border-radius:4px;overflow:hidden;"">
              {{4}}
            </table>
          </td>
        </tr>

        <!-- Callout Box -->
        <tr>
          <td style=""padding:0 24px 24px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""{calloutTableStyle}"">
              <tr>
                <td style=""padding:18px 22px;"">
                  <div style=""{calloutTitleStyle}"">{{5}}</div>
                  <div style=""font-size:14px;line-height:1.6;color:#323130;"">{{6}}</div>
                </td>
              </tr>
            </table>
          </td>
        </tr>

        <!-- CTA Button -->
        <tr>
          <td style=""padding:0 24px 28px;"">
            <a href=""{{8}}"" style=""display:inline-block;padding:10px 24px;background:#0078d4;color:#ffffff;text-decoration:none;border-radius:4px;font-size:14px;font-weight:600;font-family:'Segoe UI',sans-serif;"">
              {{7}}
            </a>
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
        public const string DetailsTableRow= @"<tr>
                <td style=""padding:10px 16px;font-size:12px;font-weight:600;color:#605e5c;text-transform:uppercase;letter-spacing:0.3px;border-bottom:1px solid #edebe9;width:140px;vertical-align:top;"">{0}</td>
                <td style=""padding:10px 16px;font-size:14px;color:#242424;border-bottom:1px solid #edebe9;{2}"">{1}</td>
              </tr>";
    }
}