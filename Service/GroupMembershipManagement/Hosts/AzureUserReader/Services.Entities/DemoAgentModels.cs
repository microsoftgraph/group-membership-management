// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Services.Entities
{
    public class DemoAgentRequest
    {
        public int AgentCount { get; set; }
        public int MaxAgentsPerUser { get; set; } = 1;
        public string TenantDomain { get; set; }
        public List<GraphProfileInformation> Managers { get; set; } = new();
    }

    public class DemoAgentSpec
    {
        public string DisplayName { get; set; }
        public string MailNickname { get; set; }
        public string UserPrincipalName { get; set; }
        public string ManagerId { get; set; }
    }

    public class ExistingDemoAgent
    {
        public string Id { get; set; }
        public string UserPrincipalName { get; set; }
        public string ManagerId { get; set; }
    }

    public class ManagerAssignment
    {
        public string AgentId { get; set; }
        public string ManagerId { get; set; }
    }

    public class DemoAgentPlan
    {
        public int Requested { get; set; }
        public int Capacity { get; set; }
        public List<DemoAgentSpec> Agents { get; set; } = new();
    }

    public class DemoAgentReconciliation
    {
        public int AlreadyPresent { get; set; }
        public List<DemoAgentSpec> ToCreate { get; set; } = new();
        public List<ManagerAssignment> ToFixManager { get; set; } = new();
    }

    public class DemoAgentWriterRequest
    {
        public string SponsorId { get; set; }
        public string BlueprintAppId { get; set; }
        public List<DemoAgentSpec> ToCreate { get; set; } = new();
        public List<ManagerAssignment> ToFixManager { get; set; } = new();
    }

    public class DemoAgentWriterResult
    {
        public int Created { get; set; }
        public int ManagersFixed { get; set; }
        public int Failed { get; set; }
    }
}
