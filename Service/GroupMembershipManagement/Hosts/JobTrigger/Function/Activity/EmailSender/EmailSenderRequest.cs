// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace JobTrigger.Activity.EmailSender
{
    public class EmailSenderRequest
    {
        public SyncJob SyncJob { get; set; }
        public string EmailSubjectTemplateName { get; set; }
        public string EmailContentTemplateName { get; set; }
        public string[] AdditionalContentParams { get; set; }
    }
}
