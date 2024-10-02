// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetSqlValidationRequest : RequestBase
    {
        public Dictionary<int, string> SqlFilters { get; }

        public GetSqlValidationRequest(Dictionary<int, string> sqlFilters)
        {
            SqlFilters = sqlFilters;
        }
    }
}