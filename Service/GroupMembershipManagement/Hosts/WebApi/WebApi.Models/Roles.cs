// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models
{
    public class Roles
    {
        public const string TENANT_ADMINISTRATOR = "MembershipManagement.ServiceConfiguration.ReadWrite.All";
        public const string TENANT_READER = "MembershipManagement.Destination.Read.All";
        public const string TENANT_SUBMISSION_REVIEWER = "MembershipManagement.Submission.ReadWrite.All";
    }
}
