// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class SqlMembershipAttributeMapping
    {
        public SqlMembershipAttributeMapping(string code, string description)
        {
            Code = code;
            Description = description;
        }

        public string Code { get; set; }
        public string Description { get; set; }
    }
}
