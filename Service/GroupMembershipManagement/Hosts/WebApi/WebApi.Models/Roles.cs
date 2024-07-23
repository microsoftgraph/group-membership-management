// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models
{
    public class Roles
    {
        public const string JOB_OWNER_READER = "Job.Read.OwnedBy";
        public const string JOB_OWNER_ENABLER = "Job.Enable.OwnedBy";
        public const string JOB_OWNER_DELETER = "Job.Delete.OwnedBy";
        public const string JOB_OWNER_CONFIGURATION_EDITOR = "Job.EditConfiguration.OwnedBy";
        public const string JOB_OWNER_WRITER = "Job.ReadWrite.OwnedBy";
        public const string JOB_TENANT_READER = "Job.Read.All";
        public const string JOB_TENANT_WRITER = "Job.ReadWrite.All";
        public const string SUBMISSION_REVIEWER = "Submission.ReadWrite.All";
        public const string HYPERLINK_ADMINISTRATOR = "Hyperlink.ReadWrite.All";
        public const string CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR = "CustomSource.ReadWrite.All";
        public const string RESET_ADMINISTRATOR = "Operations.Reset";
    }
}
