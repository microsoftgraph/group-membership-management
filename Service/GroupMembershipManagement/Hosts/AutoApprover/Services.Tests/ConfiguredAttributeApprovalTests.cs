// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.AdfRunHistory;
using Models.ServiceBus;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Services.AutoApprover;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class ConfiguredAttributeApprovalTests
    {
        private const int RequestorEmployeeId = 1;
        private const int PartManagerId = 9999;

        private sealed class Fixture
        {
            public Mock<IDatabaseSyncJobsRepository> Jobs = new();
            public Mock<IDatabaseSettingsRepository> Settings = new();
            public Mock<IGraphGroupRepository> Graph = new();
            public Mock<ISyncJobChangeRepository> Changes = new();
            public Mock<ILogger<AutoApproverService>> Logger = new();
            public Mock<ISqlMembershipRepository> Sql = new();
            public Mock<IAdfRunHistoryRepository> DataFactory = new();
            public Mock<IDatabaseSqlMembershipSourcesRepository> Sources = new();
            public Guid SyncJobId = Guid.NewGuid();
            public string RequestorId = Guid.NewGuid().ToString();

            public AutoApproverService Create() => new AutoApproverService(
                Jobs.Object, Settings.Object, Graph.Object, Changes.Object, Sql.Object, DataFactory.Object, Sources.Object,
                                Logger.Object, new Microsoft.ApplicationInsights.TelemetryClient(new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration
                {
                    ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                    TelemetryChannel = new CapturingTelemetryChannel()
                }));
        }

        private static Fixture Build(
            string requestorCompany = "100",
            string filter = null,
            bool sensitiveCompany = false,
            bool inManagerOrg = true,
            string requestorEmployeeId = "1")
        {
            var f = new Fixture();
            var filterJson = filter == null ? string.Empty : $",\"filter\":\"{filter}\"";
            f.Jobs.Setup(x => x.GetSyncJobAsync(f.SyncJobId)).ReturnsAsync(new SyncJob
            {
                Id = f.SyncJobId,
                Status = SyncStatus.PendingAutoApproval.ToString(),
                Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{PartManagerId}}}{filterJson}}}}}]"
            });

            foreach (var key in new[] { SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingKey.IsAutoApprovalForLeaderAssistantEnabled })
                f.Settings.Setup(x => x.GetSettingByKeyAsync(key)).ReturnsAsync(new Setting { SettingKey = key, SettingValue = "false" });

            f.Settings.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForConfiguredAttributeEnabled))
                .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForConfiguredAttributeEnabled, SettingValue = "true" });
            f.Settings.Setup(x => x.GetConfiguredAttributeApprovalAsync()).ReturnsAsync(new ConfiguredAttributeApprovalConfiguration
            {
                Enabled = true,
                Attributes = new List<ConfiguredAttributeApprovalRow>
                {
                    new() { Attribute = "Company", Values = new List<string> { "100", "200" } }
                }
            });

            f.DataFactory.Setup(x => x.GetLatestAdfRunAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new AdfRunHistory { AdfRunId = Guid.NewGuid(), PublishedAtUtc = DateTime.UtcNow });
            f.Sources.Setup(x => x.GetDefaultSourceAttributesAsync()).ReturnsAsync(new List<SqlMembershipAttribute>
            {
                new() { Name = "Company", Enabled = true, IsSensitive = sensitiveCompany }
            });
            f.Sql.Setup(x => x.GetColumnDetailsAsync(It.IsAny<string>()))
                 .ReturnsAsync(new List<(string Name, string Type)> { ("Company_Code", "varchar"), ("Org", "varchar") });
            var profile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Company_Code"] = requestorCompany };
            if (requestorEmployeeId != null)
                profile["EmployeeId"] = requestorEmployeeId;
            f.Sql.Setup(x => x.GetUserAttributesAsync(f.RequestorId, It.IsAny<string>())).ReturnsAsync(profile);
            f.Sql.Setup(x => x.GetChildEntitiesAsync(It.IsAny<string>(), PartManagerId, It.IsAny<string>(), 0))
                 .ReturnsAsync(inManagerOrg
                     ? new List<PersonEntity> { new() { PersonnelNumber = RequestorEmployeeId.ToString() } }
                     : new List<PersonEntity>());

            f.Logger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            return f;
        }

        private static AutoApprovalQueueMessage Message(Fixture f, AutoApprovalOrigin origin = AutoApprovalOrigin.Onboarding) =>
            new() { SyncJobId = f.SyncJobId, RequestorObjectId = f.RequestorId, RequestorDisplayName = "Requestor", Origin = origin };

        private static void AssertApproved(Fixture f)
        {
            f.Jobs.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(j => j.First().Status == SyncStatus.Idle.ToString()), null), Times.Once);
            f.Changes.Verify(x => x.Save(It.Is<SyncJobChange>(c => c.ChangeReason == SyncJobChangeReason.OnboardingAutoApproved.ToString())), Times.Once);
        }

        private static void AssertPendingReview(Fixture f)
        {
            f.Jobs.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(j => j.First().Status == SyncStatus.PendingReview.ToString()), It.IsAny<SyncStatus?>()), Times.Once);
            f.Changes.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task Onboarding_RequestorMatchesAndIsInManagerOrg_ApprovesAsync()
        {
            var f = Build();
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertApproved(f);
        }

        [TestMethod]
        public async Task Onboarding_RequestorIsTheManager_ApprovesWhenAttributesMatchAsync()
        {
            var f = Build(requestorEmployeeId: PartManagerId.ToString());
            f.Sql.Setup(x => x.GetChildEntitiesAsync(It.IsAny<string>(), PartManagerId, It.IsAny<string>(), 0))
                .ReturnsAsync(new List<PersonEntity> { new() { PersonnelNumber = PartManagerId.ToString() } });
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertApproved(f);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("not-a-number")]
        public async Task Onboarding_RequestorEmployeeIdUnavailable_StaysInReviewAsync(string employeeId)
        {
            var f = Build(requestorEmployeeId: employeeId);
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertPendingReview(f);
            f.Sql.Verify(x => x.GetChildEntitiesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }
        [TestMethod]
        public async Task Onboarding_PartWithFilter_FilterIsNotEvaluated_ApprovesAsync()
        {
            var f = Build(filter: "Org = 'X'");
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertApproved(f);
        }

        [DataTestMethod]
        [DataRow("true", false)]
        [DataRow("false", true)]
        public async Task Onboarding_SensitiveFilter_FollowsScreeningSettingAsync(string screening, bool approved)
        {
            var f = Build(filter: "Salary > 1");
            f.Sources.Setup(x => x.GetDefaultSourceAttributesAsync()).ReturnsAsync(new List<SqlMembershipAttribute>
            {
                new() { Name = "Company", Enabled = true },
                new() { Name = "Salary", Enabled = true, IsSensitive = true }
            });
            f.Settings.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsSensitiveAttributeScreeningEnabled))
                .ReturnsAsync(new Setting { SettingKey = SettingKey.IsSensitiveAttributeScreeningEnabled, SettingValue = screening });
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            if (approved)
                AssertApproved(f);
            else
                AssertPendingReview(f);
        }

        [TestMethod]
        public async Task Onboarding_ValueNotConfigured_StaysInReviewAsync()
        {
            var f = Build(requestorCompany: "300");
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertPendingReview(f);
        }

        [TestMethod]
        public async Task Onboarding_MissingRequestorValue_StaysInReviewAsync()
        {
            var f = Build(requestorCompany: "-1");
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertPendingReview(f);
        }

        [TestMethod]
        public async Task Onboarding_PartManagerNotAboveRequestor_StaysInReviewAsync()
        {
            var f = Build(inManagerOrg: false);
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertPendingReview(f);
        }

        [TestMethod]
        public async Task Onboarding_HierarchyLookupFails_StaysInReviewAsync()
        {
            var f = Build();
            f.Sql.Setup(x => x.GetChildEntitiesAsync(It.IsAny<string>(), PartManagerId, It.IsAny<string>(), 0))
                 .ThrowsAsync(new InvalidOperationException("boom"));
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertPendingReview(f);
        }

        [TestMethod]
        public async Task Onboarding_ConfiguredAttributeIsSensitive_IsApprovedAsync()
        {
            var f = Build(sensitiveCompany: true);
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertApproved(f);
        }

        [TestMethod]
        public async Task Onboarding_ConfigurationReadFails_StaysInReviewAsync()
        {
            var f = Build();
            f.Settings.Setup(x => x.GetConfiguredAttributeApprovalAsync()).ThrowsAsync(new InvalidOperationException("boom"));
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertPendingReview(f);
        }

        [TestMethod]
        public async Task Onboarding_RuleDisabled_StaysInReviewAsync()
        {
            var f = Build();
            f.Settings.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForConfiguredAttributeEnabled))
                .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForConfiguredAttributeEnabled, SettingValue = "false" });
            await f.Create().ProcessAutoApprovalAsync(Message(f));
            AssertPendingReview(f);
        }

        [TestMethod]
        public async Task Update_UsesConfiguredRuleAndRecordsUpdateAutoApprovedAsync()
        {
            var f = Build();
            await f.Create().ProcessAutoApprovalAsync(Message(f, AutoApprovalOrigin.Update));
            f.Jobs.Verify(x => x.UpdateSyncJobsAsync(It.Is<IEnumerable<SyncJob>>(j => j.First().Status == SyncStatus.Idle.ToString()), null), Times.Once);
            f.Changes.Verify(x => x.Save(It.Is<SyncJobChange>(c => c.ChangeReason == SyncJobChangeReason.UpdateAutoApproved.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task Update_ValueNotConfigured_StaysInReviewAsync()
        {
            var f = Build(requestorCompany: "300");
            await f.Create().ProcessAutoApprovalAsync(Message(f, AutoApprovalOrigin.Update));
            AssertPendingReview(f);
        }
    }
}
