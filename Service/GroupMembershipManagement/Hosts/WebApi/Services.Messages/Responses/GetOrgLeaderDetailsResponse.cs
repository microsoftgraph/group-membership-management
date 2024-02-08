// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class GetOrgLeaderDetailsResponse : ResponseBase
    {
        public int EmployeeId { get; set; }
        public int MaxDepth { get; set; }
    }
}
