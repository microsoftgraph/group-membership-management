// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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
    }
}