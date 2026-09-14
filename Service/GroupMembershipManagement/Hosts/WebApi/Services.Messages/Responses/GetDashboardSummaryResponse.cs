// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Dashboard;
using Services.Messages.Contracts.Responses;
using System.Net;

namespace Services.Messages.Responses
{
    public class GetDashboardSummaryResponse : ResponseBase
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string? ErrorCode { get; set; }
        public DashboardSummary Summary { get; set; } = new();
    }
}
