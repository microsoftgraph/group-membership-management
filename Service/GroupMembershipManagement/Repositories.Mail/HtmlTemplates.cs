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

        /// <summary>
        /// Redesigned HTML email body fragment for SyncStarted notifications.
        /// This is body-only markup (no &lt;!DOCTYPE&gt;/&lt;html&gt;/&lt;head&gt;/&lt;body&gt;); the caller is expected to wrap it
        /// inside a single top-level HTML document so the result is not a nested document.
        /// Tokens: {0}=badgeText, {1}=groupName, {2}=title, {3}=description,
        ///         {4}=detailsTableRows, {5}=calloutTitle, {6}=calloutBody,
        ///         {7}=ctaButtonLabel, {8}=ctaUrl, {9}=footerExplanation, {10}=sentDate
        /// </summary>
        public const string SyncStartedTemplate = @"<table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""background:#f3f2f1;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">
    <tr><td align=""center"" style=""padding:32px 16px;"">

      <table width=""600"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""background:#ffffff;border-radius:4px;overflow:hidden;box-shadow:0 2px 4px rgba(0,0,0,0.1);"">

        <!-- Header bar -->
        <tr>
          <td style=""background:#0078d4;padding:14px 24px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:12px;vertical-align:middle;"">
                <div style=""display:inline-block;width:36px;height:36px;background:rgba(255,255,255,0.25);border-radius:18px;text-align:center;line-height:36px;mso-line-height-rule:exactly;"">
                  <img src=""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAAoCAYAAACM/rhtAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAIXSURBVFhH7ZchTBxREIaRlUhk5UkkEll5svIkEokkQSBrmlQiK0kwJ5HINkEgkUgCCGia90/zX/+9vPtzd7x9uweI/ZJNbnP/zs6bnTczb2trYGBg4GMTEfsATgFcLrlOI2IcEdv+3MYBcAjgNgoAcB8RxxHxye30jiJ2406UAOAOwFe32RsRMQHwvOTFV4wQo8oFKFq8v3StOHLbnaFz/hYA04jYdW1ORHwG8HPJsz9cWw2dyCOn32PXrQPAnnIxd/LAda1hYuc5J+fWRm0VETHKN1ZK6YURdl0rlFc5E9e0gQ6llP40xpgmrilG0bvLjJ27pgYAv+fL/c/INUVwtWao6tPmRMSO2eTCv7uuCHYAAL9k59j/rwHAN/OPufjkunfBU6YBQALwxfVvDnNNRZz9mR3pXA72VxM/FOoCU04lut9WK+tWu/pA3ePa80V03s2dUUI/uGccAFxbA8sNcxDAWfUYBuDEHWzbg1eRlS9S151UB/82VgA8uqYG7eI5nUpMSumiMZRSorM7rmmDUmcePf52TSvokDV3Gq8+Y7DmNbZE95Th3JZb1PjVqtQoctwQuZ1eho8Zmp5z4zxfFA2cmgMXxn8tsm73rsIjoBfdKsILtVFtjWeYWTuzZ5gmrb5AMTrrVqMDVnUOF6Eus/DJX0NTTF29q0UTydm6c7I+MU+Em43aa2hS5iDRXJ3q5cDAQCH/ABG1io/gBGI4AAAAAElFTkSuQmCC"" width=""20"" height=""20"" alt="""" style=""display:inline-block;vertical-align:middle;border:0;"" />
                </div>
              </td>
              <td style=""vertical-align:middle;"">
                <span style=""color:#ffffff;font-size:16px;font-weight:600;"">Initial sync started</span><br />
                <span style=""color:#ffffffcc;font-size:13px;"">Group Membership Management</span>
              </td>
            </tr></table>
          </td>
        </tr>

        <!-- Badge -->
        <tr>
          <td style=""padding:24px 24px 4px;"">
            <span style=""display:inline-block;background:#e8f4fd;color:#0078d4;font-size:11px;font-weight:700;padding:3px 8px;border-radius:3px;letter-spacing:0.5px;"">{0}</span>
          </td>
        </tr>

        <!-- Title -->
        <tr>
          <td style=""padding:8px 24px 6px;"">
            <span style=""font-size:22px;font-weight:600;color:#242424;"">{2}</span>
          </td>
        </tr>

        <!-- Description -->
        <tr>
          <td style=""padding:4px 24px 20px;font-size:14px;line-height:1.6;color:#424242;"">
            {3}
          </td>
        </tr>

        <!-- Details Table -->
        <tr>
          <td style=""padding:0 24px 20px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border:1px solid #edebe9;border-radius:4px;overflow:hidden;"">
              {4}
            </table>
          </td>
        </tr>

        <!-- Callout Box -->
        <tr>
          <td style=""padding:0 24px 24px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-left:5px solid #0078d4;background:#deecf9;border-radius:0 4px 4px 0;"">
              <tr>
                <td style=""padding:18px 22px;"">
                  <div style=""font-size:13px;font-weight:700;color:#004578;letter-spacing:0.5px;margin-bottom:8px;"">{5}</div>
                  <div style=""font-size:14px;line-height:1.6;color:#323130;"">{6}</div>
                </td>
              </tr>
            </table>
          </td>
        </tr>

        <!-- CTA Button -->
        <tr>
          <td style=""padding:0 24px 28px;"">
            <a href=""{8}"" style=""display:inline-block;padding:10px 24px;background:#0078d4;color:#ffffff;text-decoration:none;border-radius:4px;font-size:14px;font-weight:600;font-family:'Segoe UI',sans-serif;"">
              {7}
            </a>
          </td>
        </tr>

        <!-- Divider -->
        <tr><td style=""padding:0 24px;""><hr style=""border:none;border-top:1px solid #edebe9;margin:0;""></td></tr>

        <!-- Footer explanation -->
        <tr>
          <td style=""padding:16px 24px 8px;font-size:13px;line-height:1.5;color:#605e5c;"">
            {9}
          </td>
        </tr>

        <!-- Sent date footer -->
        <tr>
          <td style=""padding:8px 24px 20px;font-size:12px;color:#a19f9d;"">
            Sent {10} &middot; Group Membership Management &middot; Microsoft
          </td>
        </tr>

      </table>
    </td></tr>
  </table>";

        /// <summary>
        /// Redesigned HTML email body fragment for SyncCompleted notifications.
        /// This is body-only markup (no &lt;!DOCTYPE&gt;/&lt;html&gt;/&lt;head&gt;/&lt;body&gt;); the caller is expected to wrap it
        /// inside a single top-level HTML document so the result is not a nested document.
        /// Uses the same token layout as SyncStartedTemplate:
        /// Tokens: {0}=badgeText, {1}=groupName, {2}=title, {3}=description,
        ///         {4}=detailsTableRows, {5}=calloutTitle, {6}=calloutBody,
        ///         {7}=ctaButtonLabel, {8}=ctaUrl, {9}=footerExplanation, {10}=sentDate
        /// </summary>
        public const string SyncCompletedTemplate = @"<table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""background:#f3f2f1;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">
    <tr><td align=""center"" style=""padding:32px 16px;"">

      <table width=""600"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""background:#ffffff;border-radius:4px;overflow:hidden;box-shadow:0 2px 4px rgba(0,0,0,0.1);"">

        <!-- Header bar -->
        <tr>
          <td style=""background:#0078d4;padding:14px 24px;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation""><tr>
              <td style=""padding-right:12px;vertical-align:middle;"">
                <span style=""display:inline-block;width:36px;height:36px;line-height:36px;text-align:center;background:rgba(255,255,255,0.25);color:#ffffff;border-radius:18px;font-size:20px;font-weight:300;"">&#x2713;</span>
              </td>
              <td style=""vertical-align:middle;"">
                <span style=""color:#ffffff;font-size:16px;font-weight:600;"">Onboarding complete</span><br />
                <span style=""color:#ffffffcc;font-size:13px;"">Group Membership Management</span>
              </td>
            </tr></table>
          </td>
        </tr>

        <!-- Badge + Group Name -->
        <tr>
          <td style=""padding:24px 24px 4px;"">
            <span style=""display:inline-block;background:#e8f4fd;color:#0078d4;font-size:11px;font-weight:700;padding:3px 8px;border-radius:3px;letter-spacing:0.5px;"">{0}</span>
          </td>
        </tr>

        <!-- Title -->
        <tr>
          <td style=""padding:8px 24px 6px;"">
            <span style=""font-size:22px;font-weight:600;color:#242424;"">{2}</span>
          </td>
        </tr>

        <!-- Description -->
        <tr>
          <td style=""padding:4px 24px 20px;font-size:14px;line-height:1.6;color:#424242;"">
            {3}
          </td>
        </tr>

        <!-- Details Table -->
        <tr>
          <td style=""padding:0 24px 20px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border:1px solid #edebe9;border-radius:4px;overflow:hidden;"">
              {4}
            </table>
          </td>
        </tr>

        <!-- Callout Box -->
        <tr>
          <td style=""padding:0 24px 24px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-left:5px solid #0078d4;background:#deecf9;border-radius:0 4px 4px 0;"">
              <tr>
                <td style=""padding:18px 22px;"">
                  <div style=""font-size:13px;font-weight:700;color:#004578;letter-spacing:0.5px;margin-bottom:8px;"">{5}</div>
                  <div style=""font-size:14px;line-height:1.6;color:#323130;"">{6}</div>
                </td>
              </tr>
            </table>
          </td>
        </tr>

        <!-- CTA Button -->
        <tr>
          <td style=""padding:0 24px 28px;"">
            <a href=""{8}"" style=""display:inline-block;padding:10px 24px;background:#0078d4;color:#ffffff;text-decoration:none;border-radius:4px;font-size:14px;font-weight:600;font-family:'Segoe UI',sans-serif;"">
              {7}
            </a>
          </td>
        </tr>

        <!-- Divider -->
        <tr><td style=""padding:0 24px;""><hr style=""border:none;border-top:1px solid #edebe9;margin:0;""></td></tr>

        <!-- Footer explanation -->
        <tr>
          <td style=""padding:16px 24px 8px;font-size:13px;line-height:1.5;color:#605e5c;"">
            {9}
          </td>
        </tr>

        <!-- Sent date footer -->
        <tr>
          <td style=""padding:8px 24px 20px;font-size:12px;color:#a19f9d;"">
            Sent {10} &middot; Group Membership Management &middot; Microsoft
          </td>
        </tr>

      </table>
    </td></tr>
  </table>";

        /// <summary>
        /// A single row for the details table in the SyncStarted/SyncCompleted templates.
        /// Tokens: {0}=label, {1}=value, {2}=optional style override for value cell
        /// </summary>
        public const string DetailsTableRow = @"<tr>
                <td style=""padding:10px 16px;font-size:12px;font-weight:600;color:#605e5c;text-transform:uppercase;letter-spacing:0.3px;border-bottom:1px solid #edebe9;width:140px;vertical-align:top;"">{0}</td>
                <td style=""padding:10px 16px;font-size:14px;color:#242424;border-bottom:1px solid #edebe9;{2}"">{1}</td>
              </tr>";
    }
}
