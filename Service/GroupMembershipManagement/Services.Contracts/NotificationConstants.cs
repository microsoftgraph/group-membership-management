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
        public const string SyncCompletedContent = "SyncCompletedEmailBody";
        public const string NoValidGroupIdsContent = "SyncDisabledNoValidGroupIds";
        public const string SyncDisabledNoGroupContent = "SyncDisabledNoSourceGroupEmailBody";
        public const string NoDataSubject = "NoDataEmailSubject";
        public const string NoDataContent = "NoDataEmailContent";
        public const string IncreaseThresholdMessage = "IncreaseThresholdMessage";
        public const string DecreaseThresholdMessage = "DecreaseThresholdMessage";
        public const string SyncJobDisabledEmailBody = "SyncJobDisabledEmailBody";
        public const string SyncThresholdEmailSubject = "SyncThresholdEmailSubject";
        public const string SyncThresholdBothEmailBody = "SyncThresholdBothEmailBody";
        public const string SyncThresholdDisablingJobEmailSubject = "SyncThresholdDisablingJobEmailSubject";
    }
}
