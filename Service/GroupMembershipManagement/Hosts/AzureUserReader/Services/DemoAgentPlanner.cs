// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Services
{
    public static class DemoAgentPlanner
    {
        public const string UpnPrefix = "gmm-demo-agent-";
        public const string DisplayNamePrefix = "GMM Demo Agent ";

        public static DemoAgentPlan Plan(IEnumerable<GraphProfileInformation> users, int agentCount, int maxAgentsPerUser, string tenantDomain)
        {
            if (maxAgentsPerUser < 1) throw new ArgumentOutOfRangeException(nameof(maxAgentsPerUser));
            var requested = Math.Max(agentCount, 0);

            var managers = (users ?? Enumerable.Empty<GraphProfileInformation>())
                .Where(u => !string.IsNullOrWhiteSpace(u?.Id))
                .Select(u => (User: u, Number: int.TryParse(u.PersonnelNumber, out var n) ? n : 0))
                .Where(x => x.Number > 0)
                .GroupBy(x => x.Number)
                .Select(g => g.First())
                .OrderBy(x => x.Number)
                .ToList();

            var capacity = (int)Math.Min((long)managers.Count * maxAgentsPerUser, int.MaxValue);
            var plan = new DemoAgentPlan { Requested = requested, Capacity = capacity };

            foreach (var (user, number) in managers)
            {
                if (plan.Agents.Count >= requested) break;

                for (var k = 1; k <= maxAgentsPerUser && plan.Agents.Count < requested; k++)
                {
                    var suffix = k == 1 ? $"{number}" : $"{number}-{k}";
                    plan.Agents.Add(new DemoAgentSpec
                    {
                        DisplayName = $"{DisplayNamePrefix}{suffix}",
                        MailNickname = $"{UpnPrefix}{suffix}",
                        UserPrincipalName = $"{UpnPrefix}{suffix}@{tenantDomain}",
                        ManagerId = user.Id
                    });
                }
            }

            return plan;
        }

        public static DemoAgentReconciliation Reconcile(DemoAgentPlan plan, IEnumerable<ExistingDemoAgent> existing)
        {
            var byUpn = new Dictionary<string, ExistingDemoAgent>(StringComparer.OrdinalIgnoreCase);
            foreach (var agent in existing ?? Enumerable.Empty<ExistingDemoAgent>())
            {
                if (!string.IsNullOrWhiteSpace(agent?.UserPrincipalName))
                    byUpn.TryAdd(agent.UserPrincipalName, agent);
            }

            var result = new DemoAgentReconciliation();
            foreach (var spec in plan.Agents)
            {
                if (!byUpn.TryGetValue(spec.UserPrincipalName, out var current))
                {
                    result.ToCreate.Add(spec);
                    continue;
                }

                result.AlreadyPresent++;
                if (!string.Equals(current.ManagerId, spec.ManagerId, StringComparison.OrdinalIgnoreCase))
                    result.ToFixManager.Add(new ManagerAssignment { AgentId = current.Id, ManagerId = spec.ManagerId });
            }

            return result;
        }
    }
}
