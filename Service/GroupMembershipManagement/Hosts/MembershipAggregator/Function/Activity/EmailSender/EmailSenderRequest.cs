// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace MembershipAggregator.Activity.EmailSender
{
    public class EmailSenderRequest
    {
        public SyncJobGroup SyncJobGroup { get; set; }
        public string EmailSubjectTemplateName { get; set; }
        public string EmailContentTemplateName { get; set; }
        public string[] AdditionalContentParams { get; set; }
        public string[] AdditionalSubjectParams { get; set; }
    }
}
