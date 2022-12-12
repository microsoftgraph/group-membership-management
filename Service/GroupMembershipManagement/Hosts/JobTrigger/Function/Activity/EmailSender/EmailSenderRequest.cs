// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;

namespace JobTrigger.Activity.EmailSender
{
    public class EmailSenderRequest
    {
        public SyncJobGroup SyncJobGroup { get; set; }
        public string EmailTemplateName { get; set; }
        public string[] AdditionalContentParams { get; set; }
    }
}
