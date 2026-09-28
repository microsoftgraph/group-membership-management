// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using DIConcreteTypes;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.Contracts;
using Repositories.SqlMembershipRepository;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    [TestCategory("SqlIntegration")]
    [DoNotParallelize]
    public class SqlAgentMembershipRepositoryIntegrationTests
    {
        private static readonly Guid _user100 =
            Guid.Parse("00000000-0000-0000-0000-000000000100");
        private static readonly Guid _user101 =
            Guid.Parse("00000000-0000-0000-0000-000000000101");
        private static readonly Guid _user102 =
            Guid.Parse("00000000-0000-0000-0000-000000000102");
        private static readonly Guid _user200 =
            Guid.Parse("00000000-0000-0000-0000-000000000200");
        private static readonly Guid _user300 =
            Guid.Parse("00000000-0000-0000-0000-000000000300");
        private static readonly Guid _agent100 =
            Guid.Parse("10000000-0000-0000-0000-000000000100");
        private static readonly Guid _agent101Disabled =
            Guid.Parse("10000000-0000-0000-0000-000000000101");
        private static readonly Guid _agent101NullBlueprint =
            Guid.Parse("20000000-0000-0000-0000-000000000101");
        private static readonly Guid _agent102 =
            Guid.Parse("10000000-0000-0000-0000-000000000102");
        private static readonly Guid _agent200 =
            Guid.Parse("10000000-0000-0000-0000-000000000200");
        private static readonly Guid _crossRunAgent300 =
            Guid.Parse("30000000-0000-0000-0000-000000000300");

        private static string _connectionString;
        private static string _tableName;
        private static string _crossRunTableName;
        private static string _deniedUserName;
        private static string _deniedUserPassword;
        private static readonly List<string> _createdTables = new();
        private static SqlMembershipRepository _repository;
        private static SqlMembershipRepository _deniedRepository;

        [ClassInitialize]
        public static async Task CreateFixtureAsync(TestContext context)
        {
            _connectionString =
                Environment.GetEnvironmentVariable("GMM_TEST_SQL_CONNECTION_STRING");

            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                Assert.Inconclusive(
                    "Set GMM_TEST_SQL_CONNECTION_STRING to a dedicated disposable SQL database.");
            }

            var connectionBuilder = new SqlConnectionStringBuilder(_connectionString);

            Assert.IsFalse(
                string.IsNullOrWhiteSpace(connectionBuilder.InitialCatalog),
                "The SQL integration connection must name a dedicated disposable database.");
            Assert.IsFalse(
                IsSystemDatabase(connectionBuilder.InitialCatalog),
                "SQL integration tests must not run against a system database.");

            var runId = Guid.NewGuid();
            _tableName = runId.ToString("N");
            _crossRunTableName = Guid.NewGuid().ToString("N");
            _repository = new SqlMembershipRepository(
                new KeyVaultSecret<ISqlMembershipRepository>(_connectionString));

            await EnsureSchemasAsync();
            await CreateUsersTableAsync(_tableName);
            await CreateAgentsTableAsync(_tableName);
            await CreateAgentsTableAsync(_crossRunTableName);
            await LoadPrimaryFixtureAsync();
            await CreateDeniedRepositoryAsync();
        }

        [ClassCleanup]
        public static async Task DropFixtureAsync()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                return;
            }

            var cleanupStatements = _createdTables
                .AsEnumerable()
                .Reverse()
                .Select(table => $"DROP TABLE IF EXISTS {table};")
                .ToList();
            cleanupStatements.Add($"""
                IF DATABASE_PRINCIPAL_ID(N'{_deniedUserName}') IS NOT NULL
                BEGIN
                    DROP USER [{_deniedUserName}];
                END;
                """);

            await ExecuteAsync(string.Join(Environment.NewLine, cleanupStatements));
        }

        [TestMethod]
        public async Task CompleteEvaluation_ProducesExpectedSetsAcrossSupportedScenarios()
        {
            var usersOnly = await EvaluateAsync(
                "EmployeeType_Code = 'FTE'",
                IdentityTypeSet.UsersOnly);
            AssertResult(
                usersOnly,
                new[] { _user100, _user101, _user200, _user300 },
                Array.Empty<Guid>(),
                4,
                IdentityType.Users);

            var agentsOnly = await EvaluateAsync(
                "EmployeeType_Code = 'FTE'",
                IdentityTypeSet.Of(IdentityType.Agents));
            AssertResult(
                agentsOnly,
                Array.Empty<Guid>(),
                new[]
                {
                    _agent100,
                    _agent101Disabled,
                    _agent101NullBlueprint,
                    _agent200,
                },
                4,
                IdentityType.Users);

            var agentFilter = await EvaluateAsync(
                "agents.BlueprintId = 'blueprint-a'",
                IdentityTypeSet.Of(IdentityType.Agents));
            AssertResult(
                agentFilter,
                Array.Empty<Guid>(),
                new[] { _agent100, _agent101Disabled, _agent200 },
                3,
                IdentityType.Agents);

            var explicitAgentFilterForUsers = await EvaluateAsync(
                "agents.BlueprintId = 'blueprint-a'",
                IdentityTypeSet.UsersOnly);
            AssertResult(
                explicitAgentFilterForUsers,
                new[] { _user100, _user101, _user200 },
                Array.Empty<Guid>(),
                3,
                IdentityType.Agents);

            var mixedAnd = await EvaluateAsync(
                "users.EmployeeType_Code = 'FTE' AND agents.BlueprintId = 'blueprint-a'",
                IdentityTypeSet.All);
            AssertResult(
                mixedAnd,
                new[] { _user100, _user101, _user200 },
                new[] { _agent100, _agent101Disabled, _agent200 },
                3,
                IdentityType.Users,
                IdentityType.Agents);

            var usersSideOr = await EvaluateAsync(
                "EmployeeType_Code = 'FTE' OR agents.BlueprintId = 'blueprint-b'",
                IdentityTypeSet.All);
            AssertResult(
                usersSideOr,
                new[] { _user100, _user101, _user102, _user200, _user300 },
                new[]
                {
                    _agent100,
                    _agent101Disabled,
                    _agent101NullBlueprint,
                    _agent102,
                    _agent200,
                },
                5,
                IdentityType.Users,
                IdentityType.Agents);

            var nullFilter = await EvaluateAsync(
                "agents.BlueprintId IS NULL",
                IdentityTypeSet.All);
            AssertResult(
                nullFilter,
                new[] { _user101, _user300 },
                new[] { _agent101NullBlueprint },
                2,
                IdentityType.Agents);

            var negatedNullFilter = await EvaluateAsync(
                "NOT (agents.BlueprintId IS NOT NULL)",
                IdentityTypeSet.All);
            AssertResult(
                negatedNullFilter,
                new[] { _user101, _user300 },
                new[] { _agent101NullBlueprint },
                2,
                IdentityType.Agents);

            var nullFilterForUsers = await EvaluateAsync(
                "agents.BlueprintId IS NULL",
                IdentityTypeSet.UsersOnly);
            AssertResult(
                nullFilterForUsers,
                new[] { _user101, _user300 },
                Array.Empty<Guid>(),
                2,
                IdentityType.Agents);

            var nullFilterForAgents = await EvaluateAsync(
                "agents.BlueprintId IS NULL",
                IdentityTypeSet.Of(IdentityType.Agents));
            AssertResult(
                nullFilterForAgents,
                Array.Empty<Guid>(),
                new[] { _agent101NullBlueprint },
                2,
                IdentityType.Agents);

            var notEqualFilter = await EvaluateAsync(
                "agents.BlueprintId <> 'blueprint-b'",
                IdentityTypeSet.All);
            AssertResult(
                notEqualFilter,
                new[] { _user100, _user101, _user200 },
                new[] { _agent100, _agent101Disabled, _agent200 },
                3,
                IdentityType.Agents);

            var notInFilter = await EvaluateAsync(
                "agents.BlueprintId NOT IN ('blueprint-b')",
                IdentityTypeSet.All);
            AssertResult(
                notInFilter,
                new[] { _user100, _user101, _user200 },
                new[] { _agent100, _agent101Disabled, _agent200 },
                3,
                IdentityType.Agents);

            var usersCollision = await EvaluateAsync(
                "ManagerId = 100",
                IdentityTypeSet.All);
            AssertResult(
                usersCollision,
                new[] { _user101 },
                new[] { _agent101Disabled, _agent101NullBlueprint },
                1,
                IdentityType.Users);

            var agentsCollision = await EvaluateAsync(
                "agents.AgentManagerId = 100",
                IdentityTypeSet.All);
            AssertResult(
                agentsCollision,
                new[] { _user100 },
                new[] { _agent100 },
                1,
                IdentityType.Agents);

            var usersAccountEnabled = await EvaluateAsync(
                "AccountEnabled = 'HumanEnabled'",
                IdentityTypeSet.All);
            AssertResult(
                usersAccountEnabled,
                new[] { _user100, _user102, _user200, _user300 },
                new[] { _agent100, _agent102, _agent200 },
                4,
                IdentityType.Users);

            var agentsAccountDisabled = await EvaluateAsync(
                "agents.AgentAccountEnabled = 0",
                IdentityTypeSet.All);
            AssertResult(
                agentsAccountDisabled,
                new[] { _user101 },
                new[] { _agent101Disabled },
                1,
                IdentityType.Agents);

            var rootOnly = await EvaluateAsync(
                string.Empty,
                IdentityTypeSet.All,
                managerEmployeeId: 100,
                depth: 1);
            AssertResult(
                rootOnly,
                new[] { _user100 },
                new[] { _agent100 },
                1);

            var rootAndChild = await EvaluateAsync(
                string.Empty,
                IdentityTypeSet.All,
                managerEmployeeId: 100,
                depth: 2);
            AssertResult(
                rootAndChild,
                new[] { _user100, _user101 },
                new[] { _agent100, _agent101Disabled, _agent101NullBlueprint },
                2);
        }

        [TestMethod]
        public async Task ProbeMatchesCompleteEvaluationForEveryFixtureIdentity()
        {
            var filters = new[]
            {
                "EmployeeType_Code = 'FTE'",
                "EmployeeType_Code = 'FTE' AND agents.AgentAccountEnabled = 0",
                "EmployeeType_Code = 'FTE' OR agents.BlueprintId = 'blueprint-b'",
                "agents.BlueprintId IS NULL",
                "agents.BlueprintId NOT IN ('blueprint-b')",
            };
            var users = new[] { _user100, _user101, _user102, _user200, _user300 };
            var agents = new[]
            {
                _agent100,
                _agent101Disabled,
                _agent101NullBlueprint,
                _agent102,
                _agent200,
            };

            foreach (var filter in filters)
            {
                var request = CreateRequest(filter, IdentityTypeSet.All);
                var complete = await _repository.EvaluateMembershipAsync(request);

                foreach (var user in users)
                {
                    Assert.AreEqual(
                        complete.UserObjectIds.Contains(user),
                        await _repository.IsIdentitySelectedAsync(
                            request,
                            user,
                            IdentityType.Users),
                        $"User probe disagreed with complete evaluation for '{filter}' and '{user:D}'.");
                }

                foreach (var agent in agents)
                {
                    Assert.AreEqual(
                        complete.AgentObjectIds.Contains(agent),
                        await _repository.IsIdentitySelectedAsync(
                            request,
                            agent,
                            IdentityType.Agents),
                        $"Agent probe disagreed with complete evaluation for '{filter}' and '{agent:D}'.");
                }
            }
        }

        [TestMethod]
        public async Task HierarchyEvaluation_AppliesFiltersOnAnyUsersColumnAfterTheWalk()
        {
            var lowerCaseColumn = await EvaluateAsync(
                "employeetype_code = 'FTE'",
                IdentityTypeSet.UsersOnly,
                managerEmployeeId: 100);
            AssertResult(
                lowerCaseColumn,
                new[] { _user100, _user101 },
                Array.Empty<Guid>(),
                2,
                IdentityType.Users);

            var hierarchyColumn = await EvaluateAsync(
                "ManagerId = 101",
                IdentityTypeSet.All,
                managerEmployeeId: 100);
            AssertResult(
                hierarchyColumn,
                new[] { _user102 },
                new[] { _agent102 },
                1,
                IdentityType.Users);

            var alreadyCarriedColumn = await EvaluateAsync(
                "EmployeeId = 101",
                IdentityTypeSet.All,
                managerEmployeeId: 100);
            AssertResult(
                alreadyCarriedColumn,
                new[] { _user101 },
                new[] { _agent101Disabled, _agent101NullBlueprint },
                1,
                IdentityType.Users);

            var usersAndAgentsColumns = await EvaluateAsync(
                "AccountEnabled = 'HumanEnabled' AND agents.BlueprintId = 'blueprint-b'",
                IdentityTypeSet.All,
                managerEmployeeId: 100);
            AssertResult(
                usersAndAgentsColumns,
                new[] { _user102 },
                new[] { _agent102 },
                1,
                IdentityType.Users,
                IdentityType.Agents);

            var filters = new[]
            {
                "employeetype_code = 'FTE'",
                "ManagerId = 101",
                "AccountEnabled = 'HumanEnabled' AND agents.BlueprintId = 'blueprint-b'",
            };

            foreach (var filter in filters)
            {
                var request = CreateRequest(filter, IdentityTypeSet.All, managerEmployeeId: 100);
                var complete = await _repository.EvaluateMembershipAsync(request);

                foreach (var user in new[] { _user100, _user101, _user102, _user200 })
                {
                    Assert.AreEqual(
                        complete.UserObjectIds.Contains(user),
                        await _repository.IsIdentitySelectedAsync(request, user, IdentityType.Users),
                        $"User probe disagreed with complete evaluation for '{filter}' and '{user:D}'.");
                }

                foreach (var agent in new[] { _agent100, _agent101Disabled, _agent102, _agent200 })
                {
                    Assert.AreEqual(
                        complete.AgentObjectIds.Contains(agent),
                        await _repository.IsIdentitySelectedAsync(request, agent, IdentityType.Agents),
                        $"Agent probe disagreed with complete evaluation for '{filter}' and '{agent:D}'.");
                }
            }
        }

        [TestMethod]
        public async Task UsersOnlyLegacyEvaluation_DoesNotRequireAgentsTable()
        {
            var usersOnlyTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(usersOnlyTable);
            await ExecuteAsync($"""
                INSERT INTO [users].[{usersOnlyTable}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (900, NULL, N'{_user100:D}', N'FTE', N'UserValue');
                """);

            var result = await _repository.EvaluateMembershipAsync(
                CreateRequest(
                    "EmployeeType_Code = 'FTE'",
                    IdentityTypeSet.UsersOnly,
                    usersOnlyTable));

            AssertResult(
                result,
                new[] { _user100 },
                Array.Empty<Guid>(),
                1,
                IdentityType.Users);

            var explicitlyQualifiedResult =
                await _repository.EvaluateMembershipAsync(
                    CreateRequest(
                        "users.EmployeeType_Code = 'FTE'",
                        IdentityTypeSet.UsersOnly,
                        usersOnlyTable));

            AssertResult(
                explicitlyQualifiedResult,
                new[] { _user100 },
                Array.Empty<Guid>(),
                1,
                IdentityType.Users);

            var deniedAgentReadResult =
                await _deniedRepository.EvaluateMembershipAsync(
                    CreateRequest(
                        "EmployeeType_Code = 'FTE'",
                        IdentityTypeSet.UsersOnly));

            AssertResult(
                deniedAgentReadResult,
                new[] { _user100, _user101, _user200, _user300 },
                Array.Empty<Guid>(),
                4,
                IdentityType.Users);
        }

        [TestMethod]
        public async Task RequiredAgentsInput_FailsClearlyWhenTheTableIsMissingOrLacksJoinColumns()
        {
            var missingAgentsTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(missingAgentsTable);
            await ExecuteAsync($"""
                INSERT INTO [users].[{missingAgentsTable}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (300, 0, N'{_user300:D}', N'FTE', N'HumanEnabled');
                """);

            var missingException = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _repository.EvaluateMembershipAsync(
                    CreateRequest(
                        string.Empty,
                        IdentityTypeSet.Of(IdentityType.Agents),
                        missingAgentsTable)));

            Assert.AreEqual("Required agents input is unavailable.", missingException.Message);

            var missingForUsersOnlyFilterException =
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                    () => _repository.EvaluateMembershipAsync(
                        CreateRequest(
                            "agents.BlueprintId = 'blueprint-a'",
                            IdentityTypeSet.UsersOnly,
                            missingAgentsTable)));

            Assert.AreEqual(
                "Required agents input is unavailable.",
                missingForUsersOnlyFilterException.Message);

            var missingForProbeException =
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                    () => _repository.IsIdentitySelectedAsync(
                        CreateRequest(
                            string.Empty,
                            IdentityTypeSet.Of(IdentityType.Agents),
                            missingAgentsTable),
                        _agent100,
                        IdentityType.Agents));

            Assert.AreEqual("Required agents input is unavailable.", missingForProbeException.Message);

            var noObjectIdTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(noObjectIdTable);
            await CreateAgentsTableWithColumnsAsync(
                noObjectIdTable,
                "AgentManagerId int NOT NULL, BlueprintId nvarchar(200) NULL");

            var noObjectIdException = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _repository.EvaluateMembershipAsync(
                    CreateRequest(
                        string.Empty,
                        IdentityTypeSet.Of(IdentityType.Agents),
                        noObjectIdTable)));

            Assert.AreEqual(
                "Required agents input is missing columns: AgentObjectId.",
                noObjectIdException.Message);

            var noManagerIdTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(noManagerIdTable);
            await CreateAgentsTableWithColumnsAsync(
                noManagerIdTable,
                "AgentObjectId nvarchar(36) NOT NULL, BlueprintId nvarchar(200) NULL");
            var noManagerIdRequest = CreateRequest(
                "agents.BlueprintId = 'blueprint-a'",
                IdentityTypeSet.UsersOnly,
                noManagerIdTable);

            var noManagerIdException = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _repository.EvaluateMembershipAsync(noManagerIdRequest));

            Assert.AreEqual(
                "Required agents input is missing columns: AgentManagerId.",
                noManagerIdException.Message);

            var noManagerIdProbeException = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _repository.IsIdentitySelectedAsync(
                    noManagerIdRequest,
                    _user300,
                    IdentityType.Users));

            Assert.AreEqual(
                "Required agents input is missing columns: AgentManagerId.",
                noManagerIdProbeException.Message);

            var noJoinColumnsTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(noJoinColumnsTable);
            await CreateAgentsTableWithColumnsAsync(
                noJoinColumnsTable,
                "BlueprintId nvarchar(200) NULL");

            var noJoinColumnsException = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _repository.EvaluateMembershipAsync(
                    CreateRequest(
                        string.Empty,
                        IdentityTypeSet.Of(IdentityType.Agents),
                        noJoinColumnsTable)));

            Assert.AreEqual(
                "Required agents input is missing columns: AgentObjectId, AgentManagerId.",
                noJoinColumnsException.Message);
        }

        [TestMethod]
        public async Task AgentsJoinColumnsWithUnsupportedTypesFailWithoutRetrying()
        {
            var guidManagerIdTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(guidManagerIdTable);
            await CreateAgentsTableWithColumnsAsync(
                guidManagerIdTable,
                "AgentObjectId nvarchar(36) NOT NULL, AgentManagerId uniqueidentifier NOT NULL");

            var textManagerIdTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(textManagerIdTable);
            await CreateAgentsTableWithColumnsAsync(
                textManagerIdTable,
                "AgentObjectId nvarchar(36) NOT NULL, AgentManagerId nvarchar(50) NOT NULL");

            var numericObjectIdTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(numericObjectIdTable);
            await CreateAgentsTableWithColumnsAsync(
                numericObjectIdTable,
                "AgentObjectId int NOT NULL, AgentManagerId int NOT NULL");

            await ExecuteAsync($"""
                INSERT INTO [users].[{textManagerIdTable}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (960, NULL, N'{_user100:D}', N'FTE', N'UserValue');

                INSERT INTO [agents].[{textManagerIdTable}]
                    (AgentObjectId, AgentManagerId)
                VALUES
                    (N'{_agent100:D}', N'not-a-number');

                INSERT INTO [users].[{numericObjectIdTable}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (961, NULL, N'{_user100:D}', N'FTE', N'UserValue');

                INSERT INTO [agents].[{numericObjectIdTable}]
                    (AgentObjectId, AgentManagerId)
                VALUES
                    (7, 961);
                """);

            var stopwatch = Stopwatch.StartNew();

            var guidManagerIdException = await Assert.ThrowsExceptionAsync<SqlException>(
                () => _repository.EvaluateMembershipAsync(
                    CreateRequest(
                        string.Empty,
                        IdentityTypeSet.Of(IdentityType.Agents),
                        guidManagerIdTable)));

            Assert.AreEqual(206, guidManagerIdException.Number);

            var textManagerIdException = await Assert.ThrowsExceptionAsync<SqlException>(
                () => _repository.EvaluateMembershipAsync(
                    CreateRequest(
                        string.Empty,
                        IdentityTypeSet.Of(IdentityType.Agents),
                        textManagerIdTable)));

            Assert.AreEqual(245, textManagerIdException.Number);

            var numericObjectIdRequest = CreateRequest(
                string.Empty,
                IdentityTypeSet.Of(IdentityType.Agents),
                numericObjectIdTable);

            var numericObjectIdProbeException = await Assert.ThrowsExceptionAsync<SqlException>(
                () => _repository.IsIdentitySelectedAsync(
                    numericObjectIdRequest,
                    _agent100,
                    IdentityType.Agents));

            Assert.AreEqual(529, numericObjectIdProbeException.Number);

            await Assert.ThrowsExceptionAsync<InvalidCastException>(
                () => _repository.EvaluateMembershipAsync(numericObjectIdRequest));

            stopwatch.Stop();

            // Retrying any one of these calls would wait 62 seconds before giving up.
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(30),
                $"Unsupported agents column types took {stopwatch.Elapsed} to fail, which suggests they were retried.");
        }

        [TestMethod]
        public async Task AgentsTableWithOnlyTheJoinColumnsOrWiderTypesIsEvaluated()
        {
            var joinColumnsOnlyTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(joinColumnsOnlyTable);
            await CreateAgentsTableWithColumnsAsync(
                joinColumnsOnlyTable,
                "AgentObjectId nvarchar(36) NOT NULL, AgentManagerId int NOT NULL");
            await ExecuteAsync($"""
                INSERT INTO [users].[{joinColumnsOnlyTable}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (950, NULL, N'{_user100:D}', N'FTE', N'UserValue');

                INSERT INTO [agents].[{joinColumnsOnlyTable}]
                    (AgentObjectId, AgentManagerId)
                VALUES
                    (N'{_agent100:D}', 950);
                """);

            var joinColumnsOnlyRequest = CreateRequest(
                string.Empty,
                IdentityTypeSet.All,
                joinColumnsOnlyTable);

            AssertResult(
                await _repository.EvaluateMembershipAsync(joinColumnsOnlyRequest),
                new[] { _user100 },
                new[] { _agent100 },
                1);
            Assert.IsTrue(
                await _repository.IsIdentitySelectedAsync(
                    joinColumnsOnlyRequest,
                    _agent100,
                    IdentityType.Agents));

            var widerTypesTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(widerTypesTable);
            await CreateAgentsTableWithColumnsAsync(
                widerTypesTable,
                "AgentObjectId nvarchar(100) NULL, AgentManagerId bigint NOT NULL");
            await ExecuteAsync($"""
                INSERT INTO [users].[{widerTypesTable}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (951, NULL, N'{_user100:D}', N'FTE', N'UserValue'),
                    (952, NULL, N'{_user101:D}', N'FTE', N'UserValue');

                INSERT INTO [agents].[{widerTypesTable}]
                    (AgentObjectId, AgentManagerId)
                VALUES
                    (N'{_agent100:D}', 951),
                    (NULL, 951);
                """);

            var widerTypesRequest = CreateRequest(
                string.Empty,
                IdentityTypeSet.All,
                widerTypesTable);
            var widerTypesResult = await _repository.EvaluateMembershipAsync(widerTypesRequest);

            AssertResult(
                widerTypesResult,
                new[] { _user100, _user101 },
                new[] { _agent100 },
                2);

            // The agent row without an object ID is skipped and counted; user 952 has no agent row, so nothing is counted for it.
            Assert.AreEqual(1, widerTypesResult.SkippedAgentRowCount);
            Assert.IsTrue(
                await _repository.IsIdentitySelectedAsync(
                    widerTypesRequest,
                    _agent100,
                    IdentityType.Agents));
            Assert.IsFalse(
                await _repository.IsIdentitySelectedAsync(
                    widerTypesRequest,
                    _agent102,
                    IdentityType.Agents));
        }

        [TestMethod]
        public async Task FilterValidation_AcceptsAgentColumnsAndChecksTheAgentsInput()
        {
            var validFilters = new Dictionary<int, string>
            {
                [0] = "EmployeeType_Code = 'FTE'",
                [1] = "agents.BlueprintId = 'blueprint-a'",
                [2] = "users.EmployeeType_Code = 'FTE' AND agents.AgentAccountEnabled = 1",
            };

            var validErrors = await _repository.ValidateFiltersAsync(
                validFilters,
                _tableName);

            Assert.AreEqual(0, validErrors.Count);

            var usersOnlyTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(usersOnlyTable);
            var missingAgentsErrors = await _repository.ValidateFiltersAsync(
                new Dictionary<int, string>
                {
                    [0] = "EmployeeType_Code = 'FTE'",
                    [1] = "agents.BlueprintId = 'blueprint-a'",
                    [2] = "EmployeeType_Cod = 'FTE'",
                    [3] = "BlueprintId = 'blueprint-a'",
                },
                usersOnlyTable);

            Assert.IsFalse(missingAgentsErrors.ContainsKey(0));
            Assert.AreEqual(
                "Required agents input is unavailable.",
                missingAgentsErrors[1]);

            // Without the agents. prefix, a column missing from the users table reads as a typo, as it did before agents.
            Assert.AreEqual(
                "Invalid column name detected: EmployeeType_Cod",
                missingAgentsErrors[2]);
            Assert.AreEqual(
                "Invalid column name detected: BlueprintId",
                missingAgentsErrors[3]);

            var deniedErrors = await _deniedRepository.ValidateFiltersAsync(
                new Dictionary<int, string>
                {
                    [0] = "EmployeeType_Code = 'FTE'",
                    [1] = "agents.BlueprintId = 'blueprint-a'",
                },
                _tableName);

            Assert.IsFalse(deniedErrors.ContainsKey(0));
            Assert.AreEqual(
                "Required agents input is unavailable.",
                deniedErrors[1]);

            var joinColumnsOnlyTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(joinColumnsOnlyTable);
            await CreateAgentsTableWithColumnsAsync(
                joinColumnsOnlyTable,
                "AgentObjectId nvarchar(36) NOT NULL, AgentManagerId int NOT NULL");
            var joinColumnsOnlyErrors = await _repository.ValidateFiltersAsync(
                new Dictionary<int, string>
                {
                    [0] = "agents.AgentManagerId = 5",
                    [1] = "agents.BlueprintId = 'blueprint-a'",
                },
                joinColumnsOnlyTable);

            Assert.IsFalse(joinColumnsOnlyErrors.ContainsKey(0));
            Assert.AreEqual(
                "Invalid column name detected: BlueprintId",
                joinColumnsOnlyErrors[1]);

            var noObjectIdTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(noObjectIdTable);
            await CreateAgentsTableWithColumnsAsync(
                noObjectIdTable,
                "AgentManagerId int NOT NULL, BlueprintId nvarchar(200) NULL");
            var noObjectIdErrors = await _repository.ValidateFiltersAsync(
                new Dictionary<int, string>
                {
                    [0] = "EmployeeType_Code = 'FTE'",
                    [1] = "agents.BlueprintId = 'blueprint-a'",
                },
                noObjectIdTable);

            Assert.IsFalse(noObjectIdErrors.ContainsKey(0));
            Assert.AreEqual(
                "Required agents input is missing columns: AgentObjectId.",
                noObjectIdErrors[1]);

            var unsafeErrors = await _repository.ValidateFiltersAsync(
                new Dictionary<int, string>
                {
                    [0] = "users.EmployeeType_Code = 'FTE'; SELECT 1",
                },
                _tableName);

            Assert.IsTrue(unsafeErrors.ContainsKey(0));

            var bareAgentColumnErrors = await _repository.ValidateFiltersAsync(
                new Dictionary<int, string>
                {
                    [0] = "BlueprintId = 'blueprint-a'",
                    [1] = "users.BlueprintId = 'blueprint-a'",
                    [2] = "agents.EmployeeType_Code = 'FTE'",
                },
                _tableName);

            Assert.IsFalse(bareAgentColumnErrors.ContainsKey(0));
            Assert.AreEqual(
                "Invalid column name detected: BlueprintId",
                bareAgentColumnErrors[1]);
            Assert.AreEqual(
                "Invalid column name detected: EmployeeType_Code",
                bareAgentColumnErrors[2]);
        }

        [TestMethod]
        public async Task ValidEmptyInputAndNoMatchesSucceedWithNoContribution()
        {
            var emptyTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(emptyTable);
            await CreateAgentsTableAsync(emptyTable);

            var emptyResult = await _repository.EvaluateMembershipAsync(
                CreateRequest(
                    string.Empty,
                    IdentityTypeSet.All,
                    emptyTable));

            AssertResult(
                emptyResult,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                0);

            var noMatches = await EvaluateAsync(
                "agents.BlueprintId = 'no-such-blueprint'",
                IdentityTypeSet.All);

            AssertResult(
                noMatches,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                0,
                IdentityType.Agents);
        }

        [TestMethod]
        public async Task DeniedAgentsInputFailsClosed()
        {
            var deniedRequest = CreateRequest(
                string.Empty,
                IdentityTypeSet.Of(IdentityType.Agents));

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _deniedRepository.EvaluateMembershipAsync(deniedRequest));

            Assert.AreEqual("Required agents input is unavailable.", exception.Message);

            var probeException = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _deniedRepository.IsIdentitySelectedAsync(
                    deniedRequest,
                    _agent100,
                    IdentityType.Agents));

            Assert.AreEqual("Required agents input is unavailable.", probeException.Message);
        }

        [TestMethod]
        public async Task EvaluationRejectsSelectedUserObjectIdThatIsNotAGuid()
        {
            var malformedUserTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(malformedUserTable);
            await ExecuteAsync($"""
                INSERT INTO [users].[{malformedUserTable}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (910, NULL, N'not-a-guid', N'FTE', N'UserValue');
                """);

            var malformedUserException =
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                    () => _repository.EvaluateMembershipAsync(
                        CreateRequest(
                            string.Empty,
                            IdentityTypeSet.UsersOnly,
                            malformedUserTable)));

            StringAssert.Contains(
                malformedUserException.Message,
                "selected AzureObjectId that is not a GUID");
        }

        [TestMethod]
        public async Task InvalidSelectedAgentObjectIds_AreSkippedAndCountedWithoutFailing()
        {
            var invalidAgentTable = Guid.NewGuid().ToString("N");
            var uppercaseAgentObjectId = _agent102.ToString("D").ToUpperInvariant();
            var signPrefixedAgent = Guid.Parse("0e984725-c51c-4bf4-9960-e1c80e27aba0");
            var signPrefixedAgentObjectId = "+" + signPrefixedAgent.ToString("D").Substring(1);
            var hexPrefixedAgent = Guid.Parse("00984725-c51c-4bf4-9960-e1c80e27aba0");
            var hexPrefixedAgentObjectId = "0x" + hexPrefixedAgent.ToString("D").Substring(2);
            await CreateUsersTableAsync(invalidAgentTable);
            await CreateAgentsTableAsync(invalidAgentTable);
            await ExecuteAsync($"""
                INSERT INTO [users].[{invalidAgentTable}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (920, NULL, N'{_user100:D}', N'FTE', N'UserValue');

                INSERT INTO [agents].[{invalidAgentTable}]
                    (AgentObjectId, AgentManagerId, BlueprintId, AgentAccountEnabled)
                VALUES
                    (N'{_agent100:D}', 920, N'blueprint-a', 1),
                    (N'{uppercaseAgentObjectId}', 920, N'blueprint-a', 1),
                    (N'{_agent200:N}', 920, N'blueprint-a', 1),
                    (N'00000000-0000-0000-0000-000000000000', 920, N'blueprint-a', 1),
                    (N'not-a-guid', 920, N'blueprint-a', 1),
                    (N'{signPrefixedAgentObjectId}', 920, N'blueprint-a', 1),
                    (N'{hexPrefixedAgentObjectId}', 920, N'blueprint-a', 1);
                """);

            var filterRequest = CreateRequest(
                "EmployeeType_Code = 'FTE'",
                IdentityTypeSet.All,
                invalidAgentTable);
            var filterResult = await _repository.EvaluateMembershipAsync(filterRequest);

            AssertResult(
                filterResult,
                new[] { _user100 },
                new[] { _agent100, _agent102 },
                1,
                IdentityType.Users);
            Assert.AreEqual(5, filterResult.SkippedAgentRowCount);

            var managerResult = await _repository.EvaluateMembershipAsync(
                CreateRequest(
                    string.Empty,
                    IdentityTypeSet.All,
                    invalidAgentTable,
                    managerEmployeeId: 920));

            AssertResult(
                managerResult,
                new[] { _user100 },
                new[] { _agent100, _agent102 },
                1);
            Assert.AreEqual(5, managerResult.SkippedAgentRowCount);

            foreach (var agent in new[] { _agent100, _agent102, _agent200, signPrefixedAgent, hexPrefixedAgent })
            {
                Assert.AreEqual(
                    filterResult.AgentObjectIds.Contains(agent),
                    await _repository.IsIdentitySelectedAsync(
                        filterRequest,
                        agent,
                        IdentityType.Agents),
                    $"Agent probe disagreed with complete evaluation for '{agent:D}'.");
            }

            Assert.IsTrue(
                await _repository.IsIdentitySelectedAsync(
                    filterRequest,
                    _user100,
                    IdentityType.Users));
        }

        [TestMethod]
        public async Task AgentObjectIdsWithSurroundingSpaces_AreSkippedByEvaluationAndProbe()
        {
            var trailingSpaceAgent = Guid.Parse("40000000-0000-0000-0000-000000000001");
            var leadingSpaceAgent = Guid.Parse("40000000-0000-0000-0000-000000000002");

            foreach (var agentObjectIdType in new[] { "nvarchar(72)", "varchar(72)" })
            {
                var spacedAgentTable = Guid.NewGuid().ToString("N");
                await CreateUsersTableAsync(spacedAgentTable);
                await CreateAgentsTableWithColumnsAsync(
                    spacedAgentTable,
                    $"AgentObjectId {agentObjectIdType} NOT NULL, AgentManagerId int NOT NULL");
                await ExecuteAsync($"""
                    INSERT INTO [users].[{spacedAgentTable}]
                        (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                    VALUES
                        (930, NULL, N'{_user100:D}', N'FTE', N'UserValue');

                    INSERT INTO [agents].[{spacedAgentTable}]
                        (AgentObjectId, AgentManagerId)
                    VALUES
                        (N'{_agent100:D}', 930),
                        (N'{trailingSpaceAgent:D} ', 930),
                        (N' {leadingSpaceAgent:D}', 930);
                    """);

                var request = CreateRequest(
                    "EmployeeType_Code = 'FTE'",
                    IdentityTypeSet.All,
                    spacedAgentTable);
                var result = await _repository.EvaluateMembershipAsync(request);

                AssertResult(
                    result,
                    new[] { _user100 },
                    new[] { _agent100 },
                    1,
                    IdentityType.Users);
                Assert.AreEqual(2, result.SkippedAgentRowCount, agentObjectIdType);

                foreach (var agent in new[] { _agent100, trailingSpaceAgent, leadingSpaceAgent })
                {
                    Assert.AreEqual(
                        result.AgentObjectIds.Contains(agent),
                        await _repository.IsIdentitySelectedAsync(
                            request,
                            agent,
                            IdentityType.Agents),
                        $"Agent probe disagreed with complete evaluation for '{agent:D}' in a {agentObjectIdType} column.");
                }
            }
        }

        [TestMethod]
        public async Task FilterWithAnUnknownTablePrefixFailsWithoutRetrying()
        {
            var stopwatch = Stopwatch.StartNew();

            var usersOnlyException = await Assert.ThrowsExceptionAsync<SqlException>(
                () => _repository.EvaluateMembershipAsync(
                    CreateRequest(
                        "nosuchtable.EmployeeType_Code = 'FTE'",
                        IdentityTypeSet.All)));

            Assert.AreEqual(4104, usersOnlyException.Number);

            var agentsException = await Assert.ThrowsExceptionAsync<SqlException>(
                () => _repository.IsIdentitySelectedAsync(
                    CreateRequest(
                        "nosuchtable.BlueprintId = 'blueprint-a'",
                        IdentityTypeSet.All),
                    _agent100,
                    IdentityType.Agents));

            Assert.AreEqual(4104, agentsException.Number);

            stopwatch.Stop();

            // Retrying either call would wait 62 seconds before giving up.
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(30),
                $"A filter with an unknown table prefix took {stopwatch.Elapsed} to fail, which suggests it was retried.");
        }

        [TestMethod]
        public async Task FilterWithAnUnprefixedColumnInBothTablesFailsWithoutRetrying()
        {
            var sharedColumnTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(sharedColumnTable);
            await CreateAgentsTableWithColumnsAsync(
                sharedColumnTable,
                "AgentObjectId nvarchar(72) NOT NULL, AgentManagerId int NOT NULL, " +
                "BlueprintId nvarchar(100) NULL, EmployeeType_Code nvarchar(50) NULL");

            const string filter = "EmployeeType_Code = 'FTE' AND agents.BlueprintId = 'blueprint-a'";
            var stopwatch = Stopwatch.StartNew();

            var evaluationException = await Assert.ThrowsExceptionAsync<SqlException>(
                () => _repository.EvaluateMembershipAsync(
                    CreateRequest(filter, IdentityTypeSet.All, sharedColumnTable)));

            Assert.AreEqual(209, evaluationException.Number);

            var probeException = await Assert.ThrowsExceptionAsync<SqlException>(
                () => _repository.IsIdentitySelectedAsync(
                    CreateRequest(filter, IdentityTypeSet.All, sharedColumnTable),
                    _agent100,
                    IdentityType.Agents));

            Assert.AreEqual(209, probeException.Number);

            stopwatch.Stop();

            // Retrying either call would wait 62 seconds before giving up.
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(30),
                $"A filter with a column in both tables took {stopwatch.Elapsed} to fail, which suggests it was retried.");
        }

        [TestMethod]
        public async Task SelectedAllZeroUserObjectId_PassesThroughAsBefore()
        {
            var allZeroUserTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(allZeroUserTable);
            await ExecuteAsync($"""
                INSERT INTO [users].[{allZeroUserTable}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (940, NULL, N'{_user100:D}', N'FTE', N'UserValue'),
                    (941, 940, N'00000000-0000-0000-0000-000000000000', N'FTE', NULL);
                """);

            var filterRequest = CreateRequest(
                "EmployeeType_Code = 'FTE'",
                IdentityTypeSet.UsersOnly,
                allZeroUserTable);

            AssertResult(
                await _repository.EvaluateMembershipAsync(filterRequest),
                new[] { _user100, Guid.Empty },
                Array.Empty<Guid>(),
                2,
                IdentityType.Users);

            AssertResult(
                await _repository.EvaluateMembershipAsync(
                    CreateRequest(
                        string.Empty,
                        IdentityTypeSet.UsersOnly,
                        allZeroUserTable,
                        managerEmployeeId: 940)),
                new[] { _user100, Guid.Empty },
                Array.Empty<Guid>(),
                2);

            Assert.IsTrue(
                await _repository.IsIdentitySelectedAsync(
                    filterRequest,
                    _user100,
                    IdentityType.Users));
        }

        [TestMethod]
        public async Task FilterEvaluationAndProbeRejectSelectedRowsWithoutEmployeeId()
        {
            var nullableEmployeeIdTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableWithNullableEmployeeIdAsync(
                nullableEmployeeIdTable);
            await ExecuteAsync($"""
                INSERT INTO [users].[{nullableEmployeeIdTable}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (NULL, NULL, N'{_user100:D}', N'FTE', N'HumanEnabled');
                """);

            var request = CreateRequest(
                "EmployeeType_Code = 'FTE'",
                IdentityTypeSet.UsersOnly,
                nullableEmployeeIdTable);

            var evaluationException =
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                    () => _repository.EvaluateMembershipAsync(request));
            StringAssert.Contains(
                evaluationException.Message,
                "selected row without an EmployeeId");

            var probeException =
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                    () => _repository.IsIdentitySelectedAsync(
                        request,
                        _user100,
                        IdentityType.Users));
            StringAssert.Contains(
                probeException.Message,
                "selected row without an EmployeeId");
        }

        [TestMethod]
        public async Task RequiredAgentsInput_FailsWhenOnlyAnotherRunHasAgentsAndUsersScopeIsEmpty()
        {
            var emptyCurrentRunTable = Guid.NewGuid().ToString("N");
            await CreateUsersTableAsync(emptyCurrentRunTable);

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _repository.EvaluateMembershipAsync(
                    CreateRequest(
                        string.Empty,
                        IdentityTypeSet.Of(IdentityType.Agents),
                        emptyCurrentRunTable)));

            Assert.AreEqual("Required agents input is unavailable.", exception.Message);
        }

        [TestMethod]
        public async Task SameRunEvaluationIgnoresMatchingAgentsFromAnotherRun()
        {
            var result = await EvaluateAsync(
                string.Empty,
                IdentityTypeSet.All);

            Assert.IsTrue(result.UserObjectIds.Contains(_user300));
            Assert.IsFalse(result.AgentObjectIds.Contains(_crossRunAgent300));
        }

        private static async Task<SqlMembershipEvaluationResult> EvaluateAsync(
            string filter,
            IdentityTypeSet identityTypes,
            int? managerEmployeeId = null,
            int depth = 0)
        {
            return await _repository.EvaluateMembershipAsync(
                CreateRequest(
                    filter,
                    identityTypes,
                    _tableName,
                    managerEmployeeId,
                    depth));
        }

        private static SqlMembershipEvaluationRequest CreateRequest(
            string filter,
            IdentityTypeSet identityTypes,
            string tableName = null,
            int? managerEmployeeId = null,
            int depth = 0)
        {
            var resolvedTableName = tableName ?? _tableName;

            return new SqlMembershipEvaluationRequest(
                managerEmployeeId,
                depth,
                filter,
                identityTypes,
                resolvedTableName,
                resolvedTableName);
        }

        private static void AssertResult(
            SqlMembershipEvaluationResult actual,
            IEnumerable<Guid> expectedUsers,
            IEnumerable<Guid> expectedAgents,
            int expectedHumanCount,
            params IdentityType[] expectedIdentityTypes)
        {
            CollectionAssert.AreEquivalent(
                expectedUsers.ToArray(),
                actual.UserObjectIds.ToArray());
            CollectionAssert.AreEquivalent(
                expectedAgents.ToArray(),
                actual.AgentObjectIds.ToArray());
            Assert.AreEqual(expectedHumanCount, actual.SelectedHumanCount);
            CollectionAssert.AreEquivalent(
                expectedIdentityTypes,
                actual.ReferencedIdentityTypes.ToArray());
        }

        private static async Task EnsureSchemasAsync()
        {
            await ExecuteAsync("""
                IF SCHEMA_ID(N'users') IS NULL
                BEGIN
                    EXEC(N'CREATE SCHEMA users');
                END;

                IF SCHEMA_ID(N'agents') IS NULL
                BEGIN
                    EXEC(N'CREATE SCHEMA agents');
                END;
                """);
        }

        private static async Task CreateUsersTableAsync(string tableName)
        {
            await ExecuteAsync($"""
                CREATE TABLE [users].[{tableName}]
                (
                    EmployeeId int NOT NULL,
                    ManagerId int NULL,
                    AzureObjectId nvarchar(36) NULL,
                    EmployeeType_Code nvarchar(50) NULL,
                    AccountEnabled nvarchar(50) NULL,
                    CONSTRAINT [PK_users_{tableName}] PRIMARY KEY (EmployeeId)
                );
                """);
            _createdTables.Add($"[users].[{tableName}]");
        }

        private static async Task CreateAgentsTableAsync(string tableName)
        {
            await ExecuteAsync($"""
                CREATE TABLE [agents].[{tableName}]
                (
                    AgentObjectId nvarchar(36) NOT NULL,
                    AgentManagerId int NOT NULL,
                    BlueprintId nvarchar(200) NULL,
                    AgentAccountEnabled bit NOT NULL
                );
                """);
            _createdTables.Add($"[agents].[{tableName}]");
        }

        private static async Task CreateUsersTableWithNullableEmployeeIdAsync(
            string tableName)
        {
            await ExecuteAsync($"""
                CREATE TABLE [users].[{tableName}]
                (
                    EmployeeId int NULL,
                    ManagerId int NULL,
                    AzureObjectId nvarchar(36) NULL,
                    EmployeeType_Code nvarchar(50) NULL,
                    AccountEnabled nvarchar(50) NULL
                );
                """);
            _createdTables.Add($"[users].[{tableName}]");
        }

        private static async Task CreateAgentsTableWithColumnsAsync(
            string tableName,
            string columnDefinitions)
        {
            await ExecuteAsync($"""
                CREATE TABLE [agents].[{tableName}]
                (
                    {columnDefinitions}
                );
                """);
            _createdTables.Add($"[agents].[{tableName}]");
        }

        private static async Task LoadPrimaryFixtureAsync()
        {
            await ExecuteAsync($"""
                INSERT INTO [users].[{_tableName}]
                    (EmployeeId, ManagerId, AzureObjectId, EmployeeType_Code, AccountEnabled)
                VALUES
                    (100, 0, N'{_user100:D}', N'FTE', N'HumanEnabled'),
                    (101, 100, N'{_user101:D}', N'FTE', N'HumanDisabled'),
                    (102, 101, N'{_user102:D}', N'Vendor', N'HumanEnabled'),
                    (200, 0, N'{_user200:D}', N'FTE', N'HumanEnabled'),
                    (300, 0, N'{_user300:D}', N'FTE', N'HumanEnabled');

                INSERT INTO [agents].[{_tableName}]
                    (AgentObjectId, AgentManagerId, BlueprintId, AgentAccountEnabled)
                VALUES
                    (N'{_agent100:D}', 100, N'blueprint-a', 1),
                    (N'{_agent100:D}', 100, N'blueprint-a', 1),
                    (N'{_agent101Disabled:D}', 101, N'blueprint-a', 0),
                    (N'{_agent101NullBlueprint:D}', 101, NULL, 1),
                    (N'{_agent102:D}', 102, N'blueprint-b', 1),
                    (N'{_agent200:D}', 200, N'blueprint-a', 1),
                    (N'40000000-0000-0000-0000-000000000999', 999, N'blueprint-a', 1);

                INSERT INTO [agents].[{_crossRunTableName}]
                    (AgentObjectId, AgentManagerId, BlueprintId, AgentAccountEnabled)
                VALUES
                    (N'{_crossRunAgent300:D}', 300, N'blueprint-a', 1);
                """);
        }

        private static async Task CreateDeniedRepositoryAsync()
        {
            _deniedUserName = "GmmDenied_" + Guid.NewGuid().ToString("N");
            _deniedUserPassword =
                "Gmm-" + Guid.NewGuid().ToString("N") + "!7aA";

            await ExecuteAsync($"""
                CREATE USER [{_deniedUserName}]
                    WITH PASSWORD = N'{_deniedUserPassword}';
                GRANT CONNECT TO [{_deniedUserName}];
                GRANT VIEW DEFINITION ON SCHEMA::[users] TO [{_deniedUserName}];
                GRANT VIEW DEFINITION ON SCHEMA::[agents] TO [{_deniedUserName}];
                GRANT SELECT ON SCHEMA::[users] TO [{_deniedUserName}];
                GRANT SELECT ON SCHEMA::[agents] TO [{_deniedUserName}];
                DENY SELECT ON OBJECT::[agents].[{_tableName}]
                    TO [{_deniedUserName}];
                """);

            var deniedConnectionBuilder =
                new SqlConnectionStringBuilder(_connectionString)
                {
                    IntegratedSecurity = false,
                    UserID = _deniedUserName,
                    Password = _deniedUserPassword,
                    Authentication = SqlAuthenticationMethod.SqlPassword,
                    PersistSecurityInfo = false,
                };

            _deniedRepository = new SqlMembershipRepository(
                new KeyVaultSecret<ISqlMembershipRepository>(
                    deniedConnectionBuilder.ConnectionString));
        }

        private static async Task ExecuteAsync(string commandText)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new SqlCommand(commandText, connection))
                {
                    command.CommandTimeout = 30;
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private static bool IsSystemDatabase(string databaseName)
        {
            return string.Equals(databaseName, "master", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(databaseName, "model", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(databaseName, "msdb", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(databaseName, "tempdb", StringComparison.OrdinalIgnoreCase);
        }
    }
}

