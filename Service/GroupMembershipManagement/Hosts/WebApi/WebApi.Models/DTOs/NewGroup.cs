// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace WebApi.Models.DTOs
{
    public class NewGroup
    {
        public NewGroup()
        {
        }
        public Guid UserIdentity { get; set; }
        public string GroupName { get; set; }
        public string GroupAlias { get; set; }
        public string? Description { get; set; }

        public NewGroupType GroupType { get; set; }
    }
}