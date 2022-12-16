// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;

namespace JobTrigger.Activity.EmailSender
{
    public class EmailSenderRequest
    {
        public SyncJobGroup SyncJobGroup { get; set; }
        public string EmailSubjectTemplateName { get; set; }
        public string EmailContentTemplateName { get; set; }
        public string[] AdditionalContentParams { get; set; }
    }
}
