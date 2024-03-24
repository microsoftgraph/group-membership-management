// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models
{
    public class Roles
    {
        public const string JOB_CREATOR = "Job.Create";
        public const string JOB_TENANT_READER = "Job.Read.All";
        public const string JOB_TENANT_WRITER = "Job.ReadWrite.All";
        public const string SUBMISSION_REVIEWER = "Submission.ReadWrite.All";
        public const string HYPERLINK_ADMINISTRATOR = "Settings.Hyperlink.ReadWrite.All";
        public const string CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR = "Settings.CustomSource.ReadWrite.All";
    }
}
