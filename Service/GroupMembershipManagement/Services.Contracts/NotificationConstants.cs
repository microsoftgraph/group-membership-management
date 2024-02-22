using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public static class NotificationConstants
    {
        public const string OnboardingSubject = "EmailSubject";
        public const string SyncStartedContent = "SyncStartedEmailBody";
        public const string DisabledNotificationSubject = "DisabledJobEmailSubject";
        public const string NotOwnerContent = "SyncDisabledNoOwnerEmailBody";
        public const string DestinationNotExistContent = "SyncDisabledNoGroupEmailBody";
    }
}
