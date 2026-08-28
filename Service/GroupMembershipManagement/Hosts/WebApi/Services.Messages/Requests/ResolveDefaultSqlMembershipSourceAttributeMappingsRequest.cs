// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    /// <summary>
    /// Resolves an explicit set of attribute codes to their descriptions so values already saved on a
    /// sync job always display correctly, independent of the capped browse page.
    /// </summary>
    public class ResolveDefaultSqlMembershipSourceAttributeMappingsRequest : RequestBase
    {
        public ResolveDefaultSqlMembershipSourceAttributeMappingsRequest(string attribute, IReadOnlyList<string> codes)
        {
            Attribute = attribute;
            Codes = codes;
        }

        public string Attribute { get; }

        public IReadOnlyList<string> Codes { get; }
    }
}
