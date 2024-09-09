// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetSqlValidationRequest : RequestBase
    {
        public string[] SqlFilters { get; }

        public GetSqlValidationRequest(string[] sqlFilters)
        {
            SqlFilters = sqlFilters;
        }
    }
}