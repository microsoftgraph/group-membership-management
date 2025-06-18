// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class GroupOwner
    {
        public GroupOwner(Guid objectId, string displayName, string mail)
        {
            ObjectId = objectId;
            DisplayName = displayName;
            Mail = mail;
        }

        public Guid ObjectId { get; set; }
        public string DisplayName { get; set; }
        public string Mail { get; set; }
    }
}
