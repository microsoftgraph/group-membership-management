// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AzureUserReader;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class DemoAgentTests
    {
        private const string Domain = "contoso.onmicrosoft.com";

        [TestMethod]
        public async Task Reader_ReturnsExistingAgents()
        {
            var agents = new List<ExistingDemoAgent> { new() { Id = "agent-id" } };
            var graph = new Mock<IDemoAgentGraphService>();
            graph.Setup(x => x.GetDemoAgentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(agents);

            var result = await new DemoAgentReaderFunction(graph.Object).RunAsync(Domain);

            Assert.AreSame(agents, result);
            graph.Verify(x => x.GetDemoAgentsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task Blueprint_ForwardsSponsor()
        {
            var graph = new Mock<IDemoAgentGraphService>();
            graph.Setup(x => x.EnsureBlueprintAsync(It.IsAny<CancellationToken>(), "sponsor-id")).ReturnsAsync("blueprint-id");

            var result = await new DemoAgentBlueprintFunction(graph.Object)
                .RunAsync(new DemoAgentWriterRequest { SponsorId = "sponsor-id" });

            Assert.AreEqual("blueprint-id", result);
            graph.Verify(x => x.EnsureBlueprintAsync(It.IsAny<CancellationToken>(), "sponsor-id"), Times.Once);
        }

        [TestMethod]
        public void Functions_PreserveDurableNames()
        {
            foreach (var type in new[]
            {
                typeof(DemoAgentReaderFunction), typeof(DemoAgentBlueprintFunction),
                typeof(DemoAgentWriterFunction), typeof(DemoAgentSubOrchestratorFunction)
            })
            {
                var attribute = (Microsoft.Azure.Functions.Worker.FunctionAttribute)Attribute.GetCustomAttribute(
                    type.GetMethod("RunAsync"), typeof(Microsoft.Azure.Functions.Worker.FunctionAttribute));
                Assert.AreEqual(type.Name, attribute.Name);
            }
        }

        private static List<GraphProfileInformation> Users(params string[] numbers)
            => numbers.Select(n => new GraphProfileInformation { Id = $"id-{n}", PersonnelNumber = n }).ToList();

        private static List<string> Upns(DemoAgentPlan plan)
            => plan.Agents.Select(a => a.UserPrincipalName.Split('@')[0]).ToList();

        [TestMethod]
        public void Plan_CountZero_IsEmpty()
        {
            var plan = DemoAgentPlanner.Plan(Users("1", "2"), 0, 1, Domain);
            Assert.AreEqual(0, plan.Agents.Count);
        }

        [TestMethod]
        public void Plan_MaxOne_TakesFirstUsersInOrder()
        {
            var plan = DemoAgentPlanner.Plan(Users("3", "1", "2"), 2, 1, Domain);
            CollectionAssert.AreEqual(new[] { "gmm-demo-agent-1", "gmm-demo-agent-2" }, Upns(plan));
            Assert.AreEqual("GMM Demo Agent 1", plan.Agents[0].DisplayName);
            Assert.AreEqual("id-1", plan.Agents[0].ManagerId);
            Assert.AreEqual($"gmm-demo-agent-1@{Domain}", plan.Agents[0].UserPrincipalName);
        }

        [TestMethod]
        public void Plan_MaxTwo_FillsEachUserBeforeNext()
        {
            var plan = DemoAgentPlanner.Plan(Users("1", "2", "3", "4"), 5, 2, Domain);
            CollectionAssert.AreEqual(
                new[] { "gmm-demo-agent-1", "gmm-demo-agent-1-2", "gmm-demo-agent-2", "gmm-demo-agent-2-2", "gmm-demo-agent-3" },
                Upns(plan));
            Assert.AreEqual("GMM Demo Agent 1-2", plan.Agents[1].DisplayName);
        }

        [TestMethod]
        public void Plan_CountAboveCapacity_CapsAtCapacity()
        {
            var plan = DemoAgentPlanner.Plan(Users("1", "2"), 10, 2, Domain);
            Assert.AreEqual(10, plan.Requested);
            Assert.AreEqual(4, plan.Capacity);
            Assert.AreEqual(4, plan.Agents.Count);
        }

        [TestMethod]
        public void Plan_ExcludesInvalidPersonnelNumbers()
        {
            var plan = DemoAgentPlanner.Plan(Users("abc", "0", "-5", "3000000000", "7"), 10, 1, Domain);
            CollectionAssert.AreEqual(new[] { "gmm-demo-agent-7" }, Upns(plan));
        }

        [TestMethod]
        public void Plan_IsDeterministic()
        {
            var a = DemoAgentPlanner.Plan(Users("5", "2", "9"), 3, 1, Domain);
            var b = DemoAgentPlanner.Plan(Users("9", "5", "2"), 3, 1, Domain);
            CollectionAssert.AreEqual(Upns(a), Upns(b));
        }

        [TestMethod]
        public void Plan_MaxBelowOne_Throws()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => DemoAgentPlanner.Plan(Users("1"), 1, 0, Domain));
        }

        [TestMethod]
        public void Reconcile_AllPresent_NoCreates()
        {
            var plan = DemoAgentPlanner.Plan(Users("1", "2"), 2, 1, Domain);
            var existing = plan.Agents.Select((a, i) => new ExistingDemoAgent { Id = $"a{i}", UserPrincipalName = a.UserPrincipalName, ManagerId = a.ManagerId });
            var result = DemoAgentPlanner.Reconcile(plan, existing);
            Assert.AreEqual(2, result.AlreadyPresent);
            Assert.AreEqual(0, result.ToCreate.Count);
            Assert.AreEqual(0, result.ToFixManager.Count);
        }

        [TestMethod]
        public void Reconcile_CreatesMissing_FixesManager_IgnoresExtras()
        {
            var plan = DemoAgentPlanner.Plan(Users("1", "2", "3"), 3, 1, Domain);
            var existing = new[]
            {
                new ExistingDemoAgent { Id = "a1", UserPrincipalName = $"GMM-DEMO-AGENT-1@{Domain}", ManagerId = "id-1" },
                new ExistingDemoAgent { Id = "a2", UserPrincipalName = $"gmm-demo-agent-2@{Domain}", ManagerId = null },
                new ExistingDemoAgent { Id = "a99", UserPrincipalName = $"gmm-demo-agent-99@{Domain}", ManagerId = "x" }
            };
            var result = DemoAgentPlanner.Reconcile(plan, existing);
            Assert.AreEqual(2, result.AlreadyPresent);
            Assert.AreEqual($"gmm-demo-agent-3@{Domain}", result.ToCreate.Single().UserPrincipalName);
            Assert.AreEqual("a2", result.ToFixManager.Single().AgentId);
            Assert.AreEqual("id-2", result.ToFixManager.Single().ManagerId);
        }

        [TestMethod]
        public async Task Writer_OneCreateFails_OthersContinue()
        {
            var graph = new Mock<IDemoAgentGraphService>();
            graph.Setup(x => x.CreateAgentAsync(It.Is<DemoAgentSpec>(s => s.DisplayName == "bad"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException());
            graph.Setup(x => x.CreateAgentAsync(It.Is<DemoAgentSpec>(s => s.DisplayName != "bad"), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("new-id");

            var functions = new DemoAgentWriterFunction(graph.Object, NullLogger<DemoAgentWriterFunction>.Instance) { Delay = _ => Task.CompletedTask };
            var result = await functions.RunAsync(new DemoAgentWriterRequest
            {
                BlueprintAppId = "bp",
                ToCreate = new List<DemoAgentSpec>
                {
                    new() { DisplayName = "ok1", ManagerId = "m" },
                    new() { DisplayName = "bad", ManagerId = "m" },
                    new() { DisplayName = "ok2", ManagerId = "m" }
                }
            });

            Assert.AreEqual(2, result.Created);
            Assert.AreEqual(1, result.Failed);
            graph.Verify(x => x.SetManagerAsync("new-id", "m", It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [TestMethod]
        public async Task Writer_ManagerRetriesThenSucceeds()
        {
            var graph = new Mock<IDemoAgentGraphService>();
            graph.SetupSequence(x => x.SetManagerAsync("a", "m", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException())
                .Returns(Task.CompletedTask);

            var functions = new DemoAgentWriterFunction(graph.Object, NullLogger<DemoAgentWriterFunction>.Instance) { Delay = _ => Task.CompletedTask };
            var result = await functions.RunAsync(new DemoAgentWriterRequest
            {
                ToFixManager = new List<ManagerAssignment> { new() { AgentId = "a", ManagerId = "m" } }
            });

            Assert.AreEqual(1, result.ManagersFixed);
            Assert.AreEqual(0, result.Failed);
        }

        [TestMethod]
        public async Task Writer_ManagerRetriesExhausted_ReportsManagerFailure()
        {
            var graph = new Mock<IDemoAgentGraphService>();
            graph.Setup(x => x.SetManagerAsync("a", "m", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException());

            var functions = new DemoAgentWriterFunction(graph.Object, NullLogger<DemoAgentWriterFunction>.Instance)
            {
                Delay = _ => Task.CompletedTask
            };
            var result = await functions.RunAsync(new DemoAgentWriterRequest
            {
                ToFixManager = new List<ManagerAssignment> { new() { AgentId = "a", ManagerId = "m" } }
            });

            Assert.AreEqual(0, result.ManagersFixed);
            Assert.AreEqual(1, result.ManagerFailures);
            Assert.AreEqual(1, result.Failed);
            graph.Verify(x => x.SetManagerAsync("a", "m", It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        private static Mock<TaskOrchestrationContext> SubOrchestratorContext(DemoAgentRequest request, List<ExistingDemoAgent> existing, DemoAgentWriterResult writerResult)
        {
            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            context.Setup(x => x.GetInput<DemoAgentRequest>()).Returns(request);
            context.Setup(x => x.CallActivityAsync<List<ExistingDemoAgent>>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>())).ReturnsAsync(existing);
            context.Setup(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>())).ReturnsAsync("bp");
            context.Setup(x => x.CallActivityAsync<DemoAgentWriterResult>(It.IsAny<TaskName>(), It.IsAny<DemoAgentWriterRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(writerResult);
            return context;
        }

        [TestMethod]
        public async Task SubOrchestrator_BatchesCreates()
        {
            var users = Enumerable.Range(1, 45).Select(i => i.ToString()).ToArray();
            var request = new DemoAgentRequest { AgentCount = 45, MaxAgentsPerUser = 1, TenantDomain = Domain, Managers = Users(users) };
            var context = SubOrchestratorContext(request, new List<ExistingDemoAgent>(), new DemoAgentWriterResult { Created = 1 });

            var result = await new DemoAgentSubOrchestratorFunction().RunAsync(context.Object);

            Assert.AreEqual(3, result.Created);
            context.Verify(x => x.CallActivityAsync<DemoAgentWriterResult>(It.IsAny<TaskName>(), It.IsAny<DemoAgentWriterRequest>(), It.IsAny<TaskOptions>()), Times.Exactly(3));
        }

        [TestMethod]
        public async Task SubOrchestrator_NothingToDo_SkipsWrites()
        {
            var request = new DemoAgentRequest { AgentCount = 1, TenantDomain = Domain, Managers = Users("1") };
            var existing = new List<ExistingDemoAgent> { new() { Id = "a", UserPrincipalName = $"gmm-demo-agent-1@{Domain}", ManagerId = "id-1" } };
            var context = SubOrchestratorContext(request, existing, new DemoAgentWriterResult());

            await new DemoAgentSubOrchestratorFunction().RunAsync(context.Object);

            context.Verify(x => x.CallActivityAsync<string>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()), Times.Never());
            context.Verify(x => x.CallActivityAsync<DemoAgentWriterResult>(It.IsAny<TaskName>(), It.IsAny<DemoAgentWriterRequest>(), It.IsAny<TaskOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task SubOrchestrator_AllCreatesFail_Throws()
        {
            var request = new DemoAgentRequest { AgentCount = 1, TenantDomain = Domain, Managers = Users("1") };
            var context = SubOrchestratorContext(request, new List<ExistingDemoAgent>(), new DemoAgentWriterResult { Failed = 1 });

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new DemoAgentSubOrchestratorFunction().RunAsync(context.Object));
        }

        [TestMethod]
        public async Task SubOrchestrator_RepairOnlyManagerFailure_ReturnsDegradedResult()
        {
            var request = new DemoAgentRequest { AgentCount = 1, TenantDomain = Domain, Managers = Users("1") };
            var existing = new List<ExistingDemoAgent>
            {
                new() { Id = "a", UserPrincipalName = $"gmm-demo-agent-1@{Domain}", ManagerId = "wrong-manager" }
            };
            var context = SubOrchestratorContext(request, existing,
                new DemoAgentWriterResult { ManagerFailures = 1, Failed = 1 });

            var result = await new DemoAgentSubOrchestratorFunction().RunAsync(context.Object);

            Assert.AreEqual(0, result.Created);
            Assert.AreEqual(0, result.ManagersFixed);
            Assert.AreEqual(1, result.ManagerFailures);
            Assert.AreEqual(1, result.Failed);
        }

        [TestMethod]
        public async Task Orchestrator_AgentCountZero_SkipsDemoAgents()
        {
            var context = new Mock<TaskOrchestrationContext>();
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            context.Setup(x => x.GetInput<AzureUserReaderRequest>()).Returns(new AzureUserReaderRequest());
            context.Setup(x => x.CallActivityAsync<IList<string>>(It.IsAny<TaskName>(), It.IsAny<AzureUserReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(new List<string> { "1" });
            context.Setup(x => x.CallSubOrchestratorAsync<List<GraphProfileInformation>>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>())).ReturnsAsync(Users("1"));

            await new OrchestratorFunction().RunOrchestrator(context.Object);

            context.Verify(x => x.CallSubOrchestratorAsync<DemoAgentWriterResult>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()), Times.Never());
        }

        [TestMethod]
        public async Task Orchestrator_AgentCountSet_RunsDemoAgentsWithUsers()
        {
            var context = new Mock<TaskOrchestrationContext>();
            var request = new AzureUserReaderRequest { AgentCount = 2, MaxAgentsPerUser = 1, TenantInformation = new TenantInformation { TenantDomain = Domain } };
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
            context.Setup(x => x.GetInput<AzureUserReaderRequest>()).Returns(request);
            context.Setup(x => x.CallActivityAsync<IList<string>>(It.IsAny<TaskName>(), It.IsAny<AzureUserReaderRequest>(), It.IsAny<TaskOptions>())).ReturnsAsync(new List<string> { "1", "2" });
            context.Setup(x => x.CallSubOrchestratorAsync<List<GraphProfileInformation>>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>())).ReturnsAsync(Users("1", "2"));
            context.Setup(x => x.CallSubOrchestratorAsync<DemoAgentWriterResult>(It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>())).ReturnsAsync(new DemoAgentWriterResult());

            await new OrchestratorFunction().RunOrchestrator(context.Object);

            context.Verify(x => x.CallSubOrchestratorAsync<DemoAgentWriterResult>(
                It.Is<TaskName>(n => n.Name == nameof(DemoAgentSubOrchestratorFunction)),
                It.Is<object>(o => ((DemoAgentRequest)o).AgentCount == 2 && ((DemoAgentRequest)o).Managers.Count == 2 && ((DemoAgentRequest)o).TenantDomain == Domain),
                It.IsAny<TaskOptions>()), Times.Once());
        }

        [TestMethod]
        public async Task Orchestrator_AgentFailures_LogDegradedWarningAndContinue()
        {
            var context = new Mock<TaskOrchestrationContext>();
            var logger = new Mock<ILogger>();
            logger.Setup(x => x.IsEnabled(LogLevel.Warning)).Returns(true);
            var request = new AzureUserReaderRequest
            {
                AgentCount = 1,
                MaxAgentsPerUser = 1,
                TenantInformation = new TenantInformation { TenantDomain = Domain }
            };
            context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(logger.Object);
            context.Setup(x => x.GetInput<AzureUserReaderRequest>()).Returns(request);
            context.Setup(x => x.CallActivityAsync<IList<string>>(
                It.IsAny<TaskName>(), It.IsAny<AzureUserReaderRequest>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(new List<string> { "1" });
            context.Setup(x => x.CallSubOrchestratorAsync<List<GraphProfileInformation>>(
                It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(Users("1"));
            context.Setup(x => x.CallSubOrchestratorAsync<DemoAgentWriterResult>(
                It.IsAny<TaskName>(), It.IsAny<object>(), It.IsAny<TaskOptions>()))
                .ReturnsAsync(new DemoAgentWriterResult { Created = 1, ManagerFailures = 1, Failed = 1 });

            await new OrchestratorFunction().RunOrchestrator(context.Object);

            logger.Verify(x => x.Log(
                LogLevel.Warning,
                It.Is<EventId>(eventId => eventId.Id == 60065),
                It.Is<It.IsAnyType>((value, _) => value.ToString().Contains("ManagerFailures=1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }
    }
}
