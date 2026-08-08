// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Services
{
    public partial class GetRunExplanationHandler
    {
        public class QueryPartInfo
        {
            public int Index { get; set; }
            public string Type { get; set; } = "Unknown";
            public string? Source { get; set; }
            public string? Filter { get; set; }
            public string? ManagerId { get; set; }
            public int? ManagerDepth { get; set; }
            public bool Exclusionary { get; set; }

            public string Key => !string.IsNullOrEmpty(Source)
                ? $"{Type}|{Source}"
                : $"{Type}|{Index}";

            public string FormatManagerScope() =>
                ManagerId == null ? "none (filter-only, no hierarchy scope)"
                : ManagerDepth.HasValue ? $"id={ManagerId} (depth<={ManagerDepth})"
                : $"id={ManagerId} (unbounded depth)";
        }
    }
}
