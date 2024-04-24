// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class RolesObject
    {
        public RolesObject()
        {

        }
        public bool IsJobOwnerReader { get; set; }
        public bool IsJobOwnerWriter { get; set; }
        public bool IsJobTenantReader { get; set; }
        public bool IsJobTenantWriter { get; set; }
        public bool IsSubmissionReviewer { get; set; }
        public bool IsHyperlinkAdministrator { get; set; }
        public bool IsCustomMembershipProviderAdministrator { get; set; }
    }
}
