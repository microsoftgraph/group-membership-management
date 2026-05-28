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
        private const string RedPillBadgeStyle =
            "display:inline-block;background:#FDE7E9;border:1px solid #F1B0B7;color:#A4262C;font-size:11px;font-weight:700;padding:3px 10px;border-radius:999px;letter-spacing:0.5px;";
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

        // Shared brand bar prepended to every fallback email: GMM people icon + title + Microsoft wordmark.
        private const string BrandHeaderHtml = @"
        <!-- Brand header bar (GMM Notification + Microsoft logo) -->
        <tr>
          <td bgcolor=""#0078D4"" style=""background:#0078D4;padding:12px 24px;"">
            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:collapse;""><tr>
              <td align=""left"" valign=""middle"" style=""vertical-align:middle;"">
                <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
                  <td width=""34"" height=""34"" align=""center"" valign=""middle"" style=""width:34px;height:34px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">
                    <img src=""data:image/png;base64,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"" width=""24"" height=""24"" alt=""GMM"" style=""display:block;border:0;outline:none;text-decoration:none;-ms-interpolation-mode:bicubic;"" />
                  </td>
                  <td style=""padding-left:12px;vertical-align:middle;"">
                    <div style=""color:#FFFFFF;font-size:10px;font-weight:700;letter-spacing:1.2px;text-transform:uppercase;line-height:1;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">__BRAND_EYEBROW__</div>
                    <div style=""color:#FFFFFF;font-size:15px;font-weight:600;line-height:1.3;margin-top:3px;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">__BRAND_WORDMARK__</div>
                  </td>
                </tr></table>
              </td>
              <td align=""right"" valign=""middle"" style=""vertical-align:middle;text-align:right;"">
                <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
                  <td style=""padding-right:8px;vertical-align:middle;"">
                    <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;"">
                      <tr>
                        <td width=""8"" height=""8"" bgcolor=""#F25022"" style=""background:#F25022;width:8px;height:8px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">&nbsp;</td>
                        <td width=""2"" style=""width:2px;line-height:0;font-size:0;"">&nbsp;</td>
                        <td width=""8"" height=""8"" bgcolor=""#7FBA00"" style=""background:#7FBA00;width:8px;height:8px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">&nbsp;</td>
                      </tr>
                      <tr><td colspan=""3"" height=""2"" style=""height:2px;line-height:0;font-size:0;"">&nbsp;</td></tr>
                      <tr>
                        <td width=""8"" height=""8"" bgcolor=""#00A4EF"" style=""background:#00A4EF;width:8px;height:8px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">&nbsp;</td>
                        <td width=""2"" style=""width:2px;line-height:0;font-size:0;"">&nbsp;</td>
                        <td width=""8"" height=""8"" bgcolor=""#FFB900"" style=""background:#FFB900;width:8px;height:8px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">&nbsp;</td>
                      </tr>
                    </table>
                  </td>
                  <td style=""vertical-align:middle;"">
                    <span style=""color:#FFFFFF;font-size:13px;font-weight:600;font-family:'Segoe UI',Helvetica,Arial,sans-serif;letter-spacing:0.2px;"">Microsoft</span>
                  </td>
                </tr></table>
              </td>
            </tr></table>
          </td>
        </tr>";

        // ── Per-template header HTML (unique icon + background + title text per type) ──
        private const string SyncStartedHeaderHtml = @"
        <!-- Quiet white status row - Awareness (blue) signal -->
        <tr>
          <td bgcolor=""#FFFFFF"" style=""background:#FFFFFF;padding:14px 24px;border-bottom:1px solid #E5E5E5;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
              <td width=""26"" height=""26"" align=""center"" valign=""middle"" bgcolor=""#EFF6FC"" style=""background:#EFF6FC;width:26px;height:26px;border-radius:13px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">
                <img src=""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAADfElEQVR4nOyZbUhTURjH/+dmYC+UGRX0QgwKxW0JkUFQFBEIUZ/8Vn0QoRcrSku2lUlZSbqsSaIVFEEEQUFRH/rQ94jsm9tCKRLpBSIyK0QH6z49c6BuN+8mnHsRzvnBznbueeE+//PynPPMgOIYUBwtABRHCwDF0QJAcbQAUBwtABRHCwDF0QJAcbQAUJwCuE0ovhOgFfxrKQhFgPjF+e88FJ9wxffKtm1DnwftpQOQiIAbBPpWQiRr2dBDEGL5tPWIUsZ1I7HwDjo8wxlloVgTp0vQ6jsFiTi/BILxAzCSAyz1OVvjUwjh4c9VFI4MIhStmewjFuH0IhzA2SUQinXxsB7FzFnEatxlEapAxiAE1aYfkwnJOCdAKPqA0/22dQg9bNwofxfzyPutFcRuLp9SX0hfss4IEIzyiAmr8US84YlWUMFThEv7M8rO9JbCNA7zUqmDi8jfBEOxCk57rAV0A0nzLNrLR2zbB6KbeGd6wa+2zNoFrqPNdxoSkT8DiNqRPVNNqkLY/ySv9gIV/zU+XSZ9wOR6gcb+VfyW2zKekTift/Gh6BEWrxsuIncGJJN7MkaJEEebNz/3lTIe4qZtHQJBMpKXAC3Iynfl0wr18WJO+XRoNttXFK8hGblrKvB+NYyxdRP5RNEbRNaMYhbjzlF4FqOvw1AcHQ/AbOfC1/kY+7F5Im8WfkB4/WdIQr4AwVglX2C22FcyCAl0IuIdQi7Ghqp5r57iThP1nHRAEvIFEKjkpN62DnFgJOLLbXyaY1mN/0AiDtwFcpzYTVGDsPce8iEYPclp2ZS+E8DcR5CIEzNg+uMqcXAk7MvT+Nhe7it7qt/na7TUGSDfC0x7XufAJ+Et8iEYO8jGP89sTsMomNsMybjoBfiKa7AAwVgHB0RuWQIigb4SDpzuYktr2PiN1vZGNVpKvkAy7rvBVMRHJOv49tfLuZ98XZ7Hz9jNJScqWCC6zYGQZ3AAZwUgpNxXOdu01VooNqS/cvVBnWjzn4BDOHESTPdJuMyjdhwfvTs4cw0zZXzHxz4njU/hhAAmv3yQjW8azz0Wf9Hqb+Dw9nYezZQH+G3fnL5xcglzDA//CfIQDiP/Otz4bi1aygZt66T/Hivnz+LJhwa7NzPKYr2Ei+h4ABRHCwDF0QJAcbQAUBwtABRHCwDF0QJAcbQAUBwtABTnHwAAAP//J+f14QAAAAZJREFUAwAo3+1lgzmGxAAAAABJRU5ErkJggg=="" width=""18"" height=""18"" alt=""Sync icon"" style=""display:block;border:0;outline:none;-ms-interpolation-mode:bicubic;"" />
              </td>
              <td style=""padding-left:12px;vertical-align:middle;"">
                <span style=""color:#201F1E;font-size:14px;font-weight:600;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">{1}</span>
              </td>
            </tr></table>
          </td>
        </tr>";

        private const string SyncCompletedHeaderHtml = @"
        <!-- Quiet white status row - Awareness (blue) signal -->
        <tr>
          <td bgcolor=""#FFFFFF"" style=""background:#FFFFFF;padding:14px 24px;border-bottom:1px solid #E5E5E5;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
              <td width=""26"" height=""26"" align=""center"" valign=""middle"" bgcolor=""#EFF6FC"" style=""background:#EFF6FC;width:26px;height:26px;border-radius:13px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">
                <img src=""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAC2UlEQVR4nOyZT2gTQRTGv9lNtS2InryICCJUkxavUtRDDrG0okj1ZA8iqBcPRaXZ4CWX2EQL4sFbQW+KolKkWnvoQdSjYLMtUgTpQYVCBVFsLMk+30gsrlm0h9nNhplfyIOd2RnyfTt/3mwsaI4FzTEGQHOMAdAcYwA0xxgAzTEGQHOMAdAcYwA0xxgAzTEGQHO0NyCBloAERtzTEDgMISZQ7L4NRQi0Ao57i+OptWvCeZS6b0IB8Z8CTnkMf4qXCLoARcR7CmRdh+PFhnKyHkMR8TXAcc9yHA2o+QSyr0IR8VwDHHeQ4z00TtFlCLsXo3sWoIj4GeCUMxwn+af5RyfhK8cDvPi9gULiNQWy5V4WPoHG31VhB/pQ6lEqHoiTAVl3L8cp/rb7K6jK4SiLf4UQUGtAzk2ihgzarEcoJBfX3W7kbRdQneEJuclXTvyBdRKl1DRCQt0a4Mz1gegJ9yj7XOWuh1BM3f9vu8vzO1CrveT7twXUnuGsbxwhoi4RIq9QFy/ZwNd3OH09/s82l95tZfEzgeKJhsMWL1GYCYov/kthc8Z2l6fFkcDbh99vgV2R4nc21BGKPOdvIALUGWDzEyNa8ZVJEzx6yFvbMV95/mMnNn57yuMlFdDTOG91OUSEOgOu9MyyYt7D6buvXJogk5rfIyFPCVQ+T3L5voBeHqA9dQ4Roj4Ryrr7eeg/4647/RW8nXn2CVjeEF8MBrScZvEDyIsqIiScTDA7fxCoTfFT7ljX/YQXWN2cwfXtK4iYcI7DpeRzWNahhjUhCKLXoER/M8RLwj0LOHNpVsh5/d/Z3RoL9cPNMppEuC9Eiim5zQ3wEP8RULsIuy3dTPGS8N8ISROE6MevA00doiXAS6PQ9QFNJrrjcG52NzyL93cWX+u4hrFdS4gBrfFSNETMHyPQHGMANMcYAM0xBkBzjAHQHGMANMcYAM0xBkBzjAHQHGMANOcnAAAA///ILIosAAAABklEQVQDALCPs6EvQBdMAAAAAElFTkSuQmCC"" width=""18"" height=""18"" alt=""Completed"" style=""display:block;border:0;outline:none;-ms-interpolation-mode:bicubic;"" />
              </td>
              <td style=""padding-left:12px;vertical-align:middle;"">
                <span style=""color:#201F1E;font-size:14px;font-weight:600;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">{1}</span>
              </td>
            </tr></table>
          </td>
        </tr>";
        private const string SyncDisabledHeaderHtml = @"
        <!-- Quiet white status row - Action (orange) signal, paused state -->
        <tr>
          <td bgcolor=""#FFFFFF"" style=""background:#FFFFFF;padding:14px 24px;border-bottom:1px solid #E5E5E5;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
              <td width=""26"" height=""26"" align=""center"" valign=""middle"" bgcolor=""#FFE8D6"" style=""background:#FFE8D6;width:26px;height:26px;border-radius:13px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">
                <img src=""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAABUElEQVR4nOzZoU7DQADG8f9dEfM8AQkSAYYEFAKBQAwFDizPwUvwCoSQLAQJm8JgQCBJSHAINGRsPa4QzGhRO/V9v2Rp7nZr2n+6LWkj4iLiHABxDoA4B0CcAyDOARDnAIhzAMQ5AOLkAyxQ0GiLXm+RfRJLeTipaq7WBzz895nbPVZioN8cWwg81ZGLzXPeKaRogHzyp3lzSPgZTytO7vqsdUXI761OI/e/45RfoWY7b44opOxXILEzO5VP8KBr+aRqOdGWfcxT0Ssg+/wzk3jtWpwvlJeW6TEF+V8AcQ6AOAdAnAMgzgEQ5wCIcwDEOQDiHABxDoC4sgECN7NTVWLUtbyaMmzZx5CCit4VTpHjWHOdEst5OK4TlxsDHrvWN88LmucG+db57vexBZ4/3jijoIA4/wYgzgEQ5wCIcwDEOQDiHABxDoA4B0CcAyDuCwAA//9ViISwAAAABklEQVQDANbXOkUehMX4AAAAAElFTkSuQmCC"" width=""18"" height=""18"" alt=""Paused"" style=""display:block;border:0;outline:none;-ms-interpolation-mode:bicubic;"" />
              </td>
              <td style=""padding-left:12px;vertical-align:middle;"">
                <span style=""color:#201F1E;font-size:14px;font-weight:600;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">{1}</span>
              </td>
            </tr></table>
          </td>
        </tr>";

        private const string SubmissionRejectedHeaderHtml = @"
        <!-- Quiet white status row - Action (orange) signal -->
        <tr>
          <td bgcolor=""#FFFFFF"" style=""background:#FFFFFF;padding:14px 24px;border-bottom:1px solid #E5E5E5;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
              <td width=""26"" height=""26"" align=""center"" valign=""middle"" bgcolor=""#FFE8D6"" style=""background:#FFE8D6;width:26px;height:26px;border-radius:13px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">
                <img src=""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAJYSURBVHhe7ZqxbhNBFEVvSUlJSQeVRZmCgh9ASWbsKA2izWfkD/IJlHwCHUbIu2MqCoQoUUThkpImItHYTvC8Hdu7s/NmZs070m289r57r0aaXWsAQRAEQRAEYT+1xkWtsDAat9GlcGM0zPQtHtG5xcAW3tXHYkvwmOXShyJL2DRZjXFuNF5FEi2gzBIcg8d4Sq+H4glfZglJClD4XmwJKQr4/Bojo/C1yBJSFGDvOz3G4yJLoEbp9VBI0F36Rn+bFK4Cao2/nrDbNKK/TwZXAUbh2hPUr4hzO8NppDrDief5oPmcEHluJ3IZyTW3QS4jueY24DZiNC7X29/V5pbHPbc1nEZqjXfO/Tf2fc65neAy4gnvlMA1tzMcRnaEfyiBY24QsY20CN9UhLnBxDQSFD7C3F7EMhIcXuN2NsYzer9kOGYCC+gTfq1P2d4KHSMBBUQIf688r8aOiY4FRAx/r/QlOAY6FMAQfqm5wns6ixXHQMsCuMI/qKWPKHQdzB2+1vhJZ7LiGNhTQIrw+zxExzGxY/hBhrc4RrYYSBD+17bZ7DhmPCa4wy91gpd0bjIcI6SAJOGtPMUno2FmrbnnMzZlLUDhT8NQauUswD55NQyl1Q/qKTmzCV7Q/+2rMd54zPZSrbEwp//OIMxPcUS9FIM9O0QD9FG2fT6UWmFKQ4RqcOG/TPCEhgjV4MJbYi3/QYa3xFj+gw0fY/kPNryl7/IfdHhLp+W/Ogp7OOG3LP/ftpT1i9FlpVaHHuz37cGnWsMcRHhLpfCchmxD0U90giAIgiAI/xN35JSGdk88UgoAAAAASUVORK5CYII="" width=""18"" height=""18"" alt=""Revision required"" style=""display:block;border:0;outline:none;-ms-interpolation-mode:bicubic;"" />
              </td>
              <td style=""padding-left:12px;vertical-align:middle;"">
                <span style=""color:#201F1E;font-size:14px;font-weight:600;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">{1}</span>
              </td>
            </tr></table>
          </td>
        </tr>";

        private const string JobPurgingWarningHeaderHtml = @"
        <!-- Quiet white status row - Warning (amber) signal -->
        <tr>
          <td bgcolor=""#FFFFFF"" style=""background:#FFFFFF;padding:14px 24px;border-bottom:1px solid #E5E5E5;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
              <td width=""26"" height=""26"" align=""center"" valign=""middle"" bgcolor=""#FFEACC"" style=""background:#FFEACC;width:26px;height:26px;border-radius:13px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">
                <img src=""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAADC0lEQVR4nOyZXUtUQRjHf3PWdnuBRKzLqFBLorsuAnNdIwkigiD8BFFWeNFF4UsJmyF5VVn0+gW6jq6imw2l6BNkVGBeRFcJFZSru6dnjqmbmO7LOXOCmR+Is7MzZ3f+53me/8xZD8vxsBwnAJbjBMBynABYjhMAy3ECYDlOACzHCYDlWC9AghgZzHDs0A6aJ6b5QEzEFgHdIr4Pd1SCu90x3ojYBGjJcEFBs/5rSnOemFDEQH87Dcrjo1I06Ne+z0xqll3ZN3zDMLFEgCw+u7j44LW08ymGiQHjEdDfKWHvM6lW5L3Ug4KvaB3NmS2IxiNAFRlTqxQ93ecVuY1hjEZAfwdHPcXztcb4RTpvjPMSQxiLAG11kutjSx0+b5O/qE/maZT2e5a/0UOTtmhMgJYOzkm4tZZ0PdJVP/uar5L/jxc79ZjmDGcxhBEBsgfZKou8vqL7Z0n7+1/v+IzoORjAiADa4kptbz0CW9xIFgNELoC2PbG3XipENke9ei4RE7kA2tpUFUVNomCD53OTiIlUgIE0Galqx6meE8E1IiQyAQIrU9ynVhauEdl+JTIBmjL0SBjvo0b0Na6k6SEiIhFAW5jcsvUON5uWWn5JexWKiuGobLGOCNAWJgI0rjVGnOHkpTaeeEmScjg6tdZYiYLtsymGpHmZkAk9t/512qsVscU5b56WkVd8IkRCTwFZ/C0VwV5e26JfV3KWCOu6hIg86enyErwoZ6xsjSdljzBWkDT0vGCjtLececUCh0cnyBESodWA4LTnlX+H5LnAxZHxhaPxQDtT8nD0WVnzPO7Jv/0EGtZOaCmwJ82ZSmzPKyw/+Ul4vCt3nv6MwTSnCYlQcrXvAPWkeCr5tLncOWJtP7qmyXXKmuZ3SnVXtJc7V8a2HdnNg9wUeWoklAhIbGFoPdtbiYTyYD7D59kOvsiC+qiMbfkiVwmBMGqALqQzkpHXqIKgCleXzXN/ptdUC2L5XeB/wv06jOU4AbAcJwCW4wTAcpwAWI4TAMtxAmA5TgAsxwmA5VgvwG8AAAD///GEDXgAAAAGSURBVAMAMryXND+wtFgAAAAASUVORK5CYII="" width=""18"" height=""18"" alt=""Action needed"" style=""display:block;border:0;outline:none;-ms-interpolation-mode:bicubic;"" />
              </td>
              <td style=""padding-left:12px;vertical-align:middle;"">
                <span style=""color:#201F1E;font-size:14px;font-weight:600;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">{1}</span>
              </td>
            </tr></table>
          </td>
        </tr>";

        // Final Notice - quiet white status row with danger (red) tile.
        // Title is supplied at format time via {1} from FinalNoticeFallback.HeaderTitle resx.
        private const string FinalNoticeHeaderHtml = @"
        <!-- Quiet white status row - Danger (red) signal -->
        <tr>
          <td bgcolor=""#FFFFFF"" style=""background:#FFFFFF;padding:14px 24px;border-bottom:1px solid #E5E5E5;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
              <td width=""26"" height=""26"" align=""center"" valign=""middle"" bgcolor=""#FDE7E9"" style=""background:#FDE7E9;width:26px;height:26px;border-radius:13px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">
                <img src=""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAADw0lEQVR4nOyaSWgUQRSG/07igmBc4r5cTUxEIQpqQAxicnIBlehEEbyInrwqigQkbjcPegroQZ1RzIAio5BFFBEJUUlAUImJYIzEFUXck/Hv7mmpzGEmM/16JlD1QdP1aqZfV/39uupVzRRAcwqgOUYAaI4RAJpjBIDmGAGgOUYAaI4RAJpjBIDmFCFPRIBVcaDJAv7S3LcDeIg8kM8IOMjOV/C8zC4jT+QtAshsr8BImIU8YcYABEQYWMgQ30iFI3XAJ2RBFCj5DdTRxw36eIMACCQC2Pkqnl7wODsM3G/I4j4NvIadf8DiuSGg5yqwGgEgLsBloIxPPsZjYqJqcTmwAhlSylmCp0V22fZFIWORhC2JqAB8SoVs7BUWp3h1HOA6Gb4dyJAQnz6vfaJUTeURjjt6yCEqAJ/SfrZuqVLVNx7YhOzZwA6/VuxKqrsXgogJwNCfwdMxz2bDh+m8divwFlnCKBgoBGrijrb//TZeAqZBCDEB6Gg33DB1YCQ0MfR74BP6eE5f5xW/JRRlF4SQfAXWqwZH7lOQ40SSXQMhxARgaC5XzK6dQC+E4DrhJf0/VaoqIYSIADFgAkNzpmezsS2Qp03xP/eOUBInIsA3Zn1QpicW+iCM6pPlAqaW8yGAiAB83yerNhv4GcLwqX9MuqfITCAiAOeoL6rNxpaM4rIur0DButN9md+ZrtqFQiKLvEeTuNj5pdgUoCzdNVT+OIX7ye8WMVk6ifSUq8ZQUkRki1hayQXQoJVY17NT3SF3o0MMrgOewVkiOP77Q+644xvJPOCWV7DTYTZ4CYSIuNNeqeL/NoQQE8BKmvr4lA5BjsOp7uUHyVQ4yk4PejYbGYq4S1pf8NVaw9MWz+Y9Br5zgwRCiAnAnP0HO31UqbLY2Oaoj/2+xLXX1Dre48geDp4QQnQ5zFGqiadWz2Zj53F2uNvMzA0Zwr2FOdwRumeNFLB1O3ABgogK0OAugbex+MqrYwfK2JGORCiPCnZ+LafITigDH6Op1/ZtOUU5RHdXPCLuFNiOpOSFXGfrw0xi2vnKvFc/sMOdQq2Ds/bBZvUzXvOBR3X9yAWRCIEIYMOwX/AHuAn/+cCjcdxd9rOxkorAfhdgg/uLgZUsnlZ3dDKAyR4a2cCqoDpvE1gEqHC7rIIdOcNiNZw0PiV2x9uo2IF6N/sLlJwI4HERKGbva1mstNyZwf55zB7U3tnzO0V6zNemhftdX5EjcirAWMT8QQKaYwSA5hgBoDlGAGiOEQCaYwSA5hgBoDlGAGiO9gL8AwAA//+uzvrTAAAABklEQVQDAMDJve8/jwe7AAAAAElFTkSuQmCC"" width=""18"" height=""18"" alt=""Final notice"" style=""display:block;border:0;outline:none;-ms-interpolation-mode:bicubic;"" />
              </td>
              <td style=""padding-left:12px;vertical-align:middle;"">
                <span style=""color:#201F1E;font-size:14px;font-weight:600;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">{1}</span>
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

        /// <summary>Body-only fragment. Same tokens as SyncStartedTemplate. Token {1}=headerText is the
        /// full localized title built in MailFallbackBuilder from SyncDisabledFallback.HeaderTitle +
        /// SyncDisabledFallback.HeaderReason.*.</summary>
        public static string SyncDisabledTemplate =>
            BuildEmailBodyTemplate(SyncDisabledHeaderHtml, OrangePillBadgeStyle, GrayCalloutTableStyle, GrayCalloutTitleStyle, GrayCalloutBgColor, GrayCalloutBorderColor);

        /// <summary>Body-only fragment. Same tokens as SyncStartedTemplate. Token {1}=headerText is
        /// rendered in the header bar (e.g. "Submission rejected — revision required").</summary>
        public static string SubmissionRejectedTemplate =>
            BuildEmailBodyTemplate(SubmissionRejectedHeaderHtml, OrangePillBadgeStyle, GrayCalloutTableStyle, GrayCalloutTitleStyle, GrayCalloutBgColor, GrayCalloutBorderColor);

        /// <summary>Body-only fragment. Same tokens as SyncStartedTemplate. Token {1}=headerText is rendered
        /// in the header bar with a localized title (e.g. "Sync job will be purged soon").</summary>
        public static string JobPurgingWarningTemplate =>
            BuildEmailBodyTemplate(JobPurgingWarningHeaderHtml, OrangePillBadgeStyle, GrayCalloutTableStyle, GrayCalloutTitleStyle, GrayCalloutBgColor, GrayCalloutBorderColor);

        /// <summary>Body-only fragment. Same tokens as SyncStartedTemplate. Token {1}=headerText is rendered
        /// in the dark red header bar (e.g. "Final notice — GMM's affiliation with this group is removed").
        /// Final Notice does NOT render the gray "what happens if you do nothing" callout - the action
        /// checklist already explains next steps - so the {5}/{6} callout slots are omitted.</summary>
        public static string FinalNoticeTemplate =>
            BuildEmailBodyTemplate(FinalNoticeHeaderHtml, RedPillBadgeStyle, GrayCalloutTableStyle, GrayCalloutTitleStyle, GrayCalloutBgColor, GrayCalloutBorderColor, includeCallout: false);

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
            string calloutBorderColor,
            bool includeCallout = true) =>
            $@"<table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""background:#f3f2f1;font-family:'Segoe UI',Helvetica,Arial,sans-serif;"">
    <tr><td align=""center"" style=""padding:32px 16px;"">

      <table width=""600"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""background:#ffffff;border-radius:4px;overflow:hidden;box-shadow:0 2px 4px rgba(0,0,0,0.1);"">
{BrandHeaderHtml}
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

        <!-- Optional Extra Callout (e.g. nested groups detected) -->
        {{12}}

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
{(includeCallout ? $@"
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
        </tr>" : string.Empty)}

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
          <td style=""padding:8px 24px 20px;font-size:12px;color:#605e5c;"">
            <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
              <td valign=""middle"" style=""vertical-align:middle;font-size:12px;color:#a19f9d;padding-right:8px;"">Sent {{10}} &nbsp;&middot;&nbsp;</td>
              <td valign=""middle"" style=""vertical-align:middle;padding-right:6px;"">
                <table cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:separate;""><tr>
                  <td width=""18"" height=""18"" align=""center"" valign=""middle"" bgcolor=""#0078D4"" style=""background:#0078D4;width:18px;height:18px;border-radius:3px;line-height:0;font-size:0;mso-line-height-rule:exactly;"">
                    <img src=""data:image/png;base64,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"" width=""12"" height=""12"" alt=""GMM"" style=""display:block;border:0;outline:none;text-decoration:none;-ms-interpolation-mode:bicubic;"" />
                  </td>
                </tr></table>
              </td>
              <td valign=""middle"" style=""vertical-align:middle;font-size:12px;color:#605e5c;font-weight:600;"">__BRAND_WORDMARK__</td>
              <td valign=""middle"" style=""vertical-align:middle;font-size:12px;color:#605e5c;padding-left:8px;"">&nbsp;&middot;&nbsp; Microsoft</td>
            </tr></table>
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

        // ── Extra gray callout (same styling as "What happens if you do nothing") ────
        // Used to render the inline "Nested groups detected · N total" block under
        // the description in the Sync Disabled (NestedGroupsFound) fallback.
        // Tokens: {0}=callout title (HTML-encoded), {1}=callout body HTML (e.g. <ul><li>...</li></ul>).
        public const string GrayExtraCalloutHtml = @"
        <tr>
          <td style=""padding:0 24px 16px;"">
            <div style=""border:1px solid #E0E0E0;border-radius:4px;overflow:hidden;"">
              <table width=""100%"" cellpadding=""0"" cellspacing=""0"" role=""presentation"" style=""border-collapse:collapse;mso-table-lspace:0pt;mso-table-rspace:0pt;"">
                <tr>
                  <td width=""3"" bgcolor=""#8A8A8A"" style=""background:#8A8A8A;width:3px;line-height:0;font-size:0;"">&nbsp;</td>
                  <td bgcolor=""#F5F5F5"" style=""background:#F5F5F5;padding:14px 16px;"">
                    <div style=""font-size:13px;font-weight:700;color:#323130;letter-spacing:0.5px;text-transform:uppercase;margin-bottom:8px;"">{0}</div>
                    <div style=""font-size:13.5px;line-height:1.55;color:#605E5C;"">{1}</div>
                  </td>
                </tr>
              </table>
            </div>
          </td>
        </tr>";
    }
}