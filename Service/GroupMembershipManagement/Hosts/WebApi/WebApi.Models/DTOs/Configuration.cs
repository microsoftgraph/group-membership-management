// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class Configuration
    {
        public Configuration(Guid id, string? dashboardUrl)
        {
            Id = id;
            DashboardUrl = dashboardUrl;
        }
        public Guid Id { get; set; }
        public string? DashboardUrl { get; set; }
    }
}
