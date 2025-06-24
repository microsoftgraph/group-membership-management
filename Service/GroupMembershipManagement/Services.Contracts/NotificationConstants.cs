using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public static class NotificationConstants
    {
        public const string OnboardingStartedEmailTitle = "OnboardingStartedEmailTitle";
        public const string OnboardingStartedEmailSubject = "OnboardingStartedEmailSubject";
        public const string OnboardingCompleteEmailTitle = "OnboardingCompleteEmailTitle";
        public const string OnboardingCompleteEmailSubject = "OnboardingCompleteEmailSubject";
        public const string SyncStartedContent = "SyncStartedEmailBody";
        public const string NotOwnerTitle = "NotOwnerTitle";
        public const string NotValidSourceTitle = "NotValidSourceTitle";
        public const string NotValidSourceSubject = "NotValidSourceSubject";
        public const string SourceNotExistTitle = "SourceNotExistTitle";
        public const string GuestUserFailureTitle = "GuestUserFailureTitle";
        public const string DestinationNotExistTitle = "DestinationNotExistTitle";
        public const string DisabledNotificationSubject = "DisabledJobEmailSubject";
        public const string NotOwnerContent = "SyncDisabledNoOwnerEmailBody";
        public const string DestinationNotExistContent = "SyncDisabledNoGroupEmailBody";
        public const string DestinationNotExistSubject = "DestinationNotExistSubject";
        public const string SyncCompletedContent = "SyncCompletedEmailBody";
        public const string NoValidGroupIdsContent = "SyncDisabledNoValidGroupIds";
        public const string SyncDisabledNoGroupContent = "SyncDisabledNoSourceGroupEmailBody";
        public const string NoDataTitle = "NoDataEmailTitle";
        public const string NoDataSubject = "NoDataEmailSubject";
        public const string NoDataContent = "NoDataEmailContent";
        public const string IncreaseThresholdMessage = "IncreaseThresholdMessage";
        public const string DecreaseThresholdMessage = "DecreaseThresholdMessage";
        public const string SyncJobDisabledEmailBody = "SyncJobDisabledEmailBody";
        public const string SyncThresholdEmailSubject = "SyncThresholdEmailSubject";
        public const string SyncThresholdBothEmailBody = "SyncThresholdBothEmailBody";
        public const string SyncThresholdDisablingJobEmailSubject = "SyncThresholdDisablingJobEmailSubject";
        public const string SyncPurgedForInactivityEmailBody = "SyncPurgedForInactivityEmailBody";
        public const string SyncPurgedForInactivityEmailSubject = "SyncPurgedForInactivityEmailSubject";
        public const string SyncPurgedForInactivityEmailTitle = "SyncPurgedForInactivityEmailTitle";
        public const string GuestUserFailureEmailBody = "GuestUserFailureEmailBody";
        public const string ThresholdNotificationFallbackBody = "ThresholdNotificationFallbackBody";
        public const string SubmissionRejectedEmailTitle = "SubmissionRejectedEmailTitle";
        public const string SubmissionRejectedEmailSubject = "SubmissionRejectedEmailSubject";
        public const string SubmissionRejectedEmailBody = "SubmissionRejectedEmailBody";
    }
}
