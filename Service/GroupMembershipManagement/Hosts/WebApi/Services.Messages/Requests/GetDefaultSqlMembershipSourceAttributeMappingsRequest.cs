// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetDefaultSqlMembershipSourceAttributeMappingsRequest : RequestBase
    {
        public GetDefaultSqlMembershipSourceAttributeMappingsRequest(string attribute, string? search = null, int? top = null)
        {
            Attribute = attribute;
            Search = search;
            Top = top;
        }

        public string Attribute { get; }

        /// <summary>Optional prefix filter applied to Code or Description by the database.</summary>
        public string? Search { get; }

        /// <summary>Optional page size. Falls back to the repository default when not supplied.</summary>
        public int? Top { get; }
    }
}