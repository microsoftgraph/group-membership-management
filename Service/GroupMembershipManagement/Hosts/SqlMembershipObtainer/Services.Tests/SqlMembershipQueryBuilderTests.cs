// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.SqlMembershipRepository;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Services.Tests.SqlFilterTestHelpers;

namespace Services.Tests
{
    [TestClass]
    public sealed class SqlMembershipQueryBuilderTests
    {
        [TestMethod]
        public void MembershipQuery_AgentFilterUsesReadableAliasesAndVerbatimFilterAfterJoin()
        {
            const string filter =
                "users.Department = 'Sales' AND agents.BlueprintId = 'alpha'";
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: 42,
                depth: 3,
                filter,
                IdentityTypeSet.All,
                tableName: "run123");

            var plan = SqlMembershipQueryBuilder.BuildEvaluation(
                request,
                Analyze(filter));

            Assert.IsTrue(plan.RequiresAgents);
            Assert.IsTrue(plan.ReturnsAgentColumns);
            StringAssert.Contains(
                plan.CommandText,
                "SELECT [source].[AzureObjectId], [source].[EmployeeId], [source].[Department], CAST(1 AS int) AS [Depth]");
            StringAssert.Contains(
                plan.CommandText,
                "SELECT [child].[AzureObjectId], [child].[EmployeeId], [child].[Department], [parent].[Depth] + 1");
            StringAssert.Contains(
                plan.CommandText,
                "WHERE [parent].[Depth] < @Depth");
            StringAssert.Contains(
                plan.CommandText,
                "FROM [UserScope] AS [users]");
            StringAssert.Contains(
                plan.CommandText,
                "LEFT JOIN [agents].[run123] AS [agents]");
            StringAssert.Contains(
                plan.CommandText,
                "ON [agents].[AgentManagerId] = [users].[EmployeeId]");
            StringAssert.Contains(
                plan.CommandText,
                "WHERE (users.Department = 'Sales' AND agents.BlueprintId = 'alpha')");
            StringAssert.Contains(
                plan.CommandText,
                "[agents].[AgentObjectId] AS [AgentObjectId]");
            StringAssert.Contains(
                plan.CommandText,
                "CAST(CASE WHEN [agents].[AgentManagerId] IS NULL THEN 0 ELSE 1 END AS bit) AS [HasAgentRow]");
            Assert.IsFalse(plan.CommandText.Contains("@filter", StringComparison.Ordinal));
            Assert.IsFalse(plan.CommandText.Contains("[u].", StringComparison.Ordinal));
            Assert.IsFalse(plan.CommandText.Contains("[a].", StringComparison.Ordinal));
            AssertValidSql(plan.CommandText);
        }

        [TestMethod]
        public void MembershipQuery_AbsentFilterMatchesTheCompleteScopedCandidateSet()
        {
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: 42,
                depth: 1,
                filter: string.Empty,
                IdentityTypeSet.All,
                tableName: "run123");
            var plan = SqlMembershipQueryBuilder.BuildEvaluation(
                request,
                SqlFilterCompilationResult.Valid(Array.Empty<IdentityType>(), Array.Empty<string>()));

            Assert.IsTrue(plan.RequiresAgents);
            StringAssert.Contains(
                plan.CommandText,
                "SELECT [source].[AzureObjectId], [source].[EmployeeId], CAST(1 AS int) AS [Depth]");
            StringAssert.Contains(
                plan.CommandText,
                "WHERE [source].[EmployeeId] = @ManagerEmployeeId");
            StringAssert.Contains(
                plan.CommandText,
                "WHERE [parent].[Depth] < @Depth");
            Assert.IsFalse(
                plan.CommandText.Contains(
                    "[FilteredUsers] AS",
                    StringComparison.Ordinal));
            AssertValidSql(plan.CommandText);
        }

        [TestMethod]
        public void MembershipQuery_AppliesLegacyUsersFilterBeforeTheAgentsJoin()
        {
            const string filter = "Department LIKE 'Sales%'";
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter,
                IdentityTypeSet.Of(IdentityType.Agents),
                tableName: "run123");
            var plan = SqlMembershipQueryBuilder.BuildEvaluation(
                request,
                Analyze(filter));

            var filterIndex = plan.CommandText.IndexOf(
                "WHERE (Department LIKE 'Sales%')",
                StringComparison.Ordinal);
            var joinIndex = plan.CommandText.IndexOf(
                "LEFT JOIN",
                StringComparison.Ordinal);

            Assert.IsTrue(plan.RequiresAgents);
            Assert.IsTrue(filterIndex >= 0);
            Assert.IsTrue(joinIndex > filterIndex);
            StringAssert.Contains(
                plan.CommandText,
                "FROM [UserScope] AS [users]");
            StringAssert.Contains(
                plan.CommandText,
                "LEFT JOIN [agents].[run123] AS [agents]");
            Assert.AreEqual(
                1,
                plan.CommandText.Split(
                    "Department LIKE 'Sales%'",
                    StringSplitOptions.None).Length - 1);
            AssertValidSql(plan.CommandText);
        }

        [TestMethod]
        public void MembershipQuery_DoesNotReadAgentsForExplicitUsersOnlyEvaluation()
        {
            const string filter = "users.Department = 'Sales'";
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter,
                IdentityTypeSet.UsersOnly,
                tableName: "run123");
            var plan = SqlMembershipQueryBuilder.BuildEvaluation(
                request,
                Analyze(filter));

            Assert.IsFalse(plan.RequiresAgents);
            Assert.IsFalse(plan.ReturnsAgentColumns);
            Assert.IsFalse(
                plan.CommandText.Contains("[agents]", StringComparison.Ordinal));
            StringAssert.Contains(
                plan.CommandText,
                "FROM [UserScope] AS [users]");
            StringAssert.Contains(
                plan.CommandText,
                "WHERE (users.Department = 'Sales')");
            AssertValidSql(plan.CommandText);
        }

        [TestMethod]
        public void MembershipQuery_ReadsAgentsForAnAgentPredicateWithoutProjectingThem()
        {
            const string filter = "agents.BlueprintId = 'alpha'";
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter,
                IdentityTypeSet.UsersOnly,
                tableName: "run123");
            var plan = SqlMembershipQueryBuilder.BuildEvaluation(
                request,
                Analyze(filter));

            Assert.IsTrue(plan.RequiresAgents);
            Assert.IsFalse(plan.ReturnsAgentColumns);
            StringAssert.Contains(plan.CommandText, "LEFT JOIN [agents].[run123] AS [agents]");
            Assert.IsFalse(
                plan.CommandText.Contains(
                    "AS [AgentObjectId]",
                    StringComparison.Ordinal));
            AssertValidSql(plan.CommandText);
        }

        [TestMethod]
        public void MembershipQuery_CarriesOnlyTheColumnsItReadsThroughEachStep()
        {
            const string filter = "department = 'Sales' AND C_Wide = 'x' AND employeeid > 0";
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter,
                IdentityTypeSet.UsersOnly,
                tableName: "run123");
            var compilation = SqlMembershipRepository.CheckFilter(
                filter,
                "run123",
                UserColumnNames.Append("C_Wide").ToArray(),
                AgentColumnNames);

            Assert.IsTrue(compilation.IsValid, compilation.Error);

            var plan = SqlMembershipQueryBuilder.BuildEvaluation(request, compilation);

            StringAssert.Contains(
                plan.CommandText,
                "SELECT [source].[AzureObjectId], [source].[EmployeeId], [source].[Department], [source].[C_Wide]");
            Assert.AreEqual(1, CountOccurrences(plan.CommandText, "[source].[EmployeeId]"));
            StringAssert.Contains(
                plan.CommandText,
                "SELECT [users].[AzureObjectId], [users].[EmployeeId]");
            Assert.IsFalse(plan.CommandText.Contains("[users].[Department]", StringComparison.Ordinal));
            Assert.IsFalse(plan.CommandText.Contains("[users].[C_Wide]", StringComparison.Ordinal));
            Assert.IsFalse(plan.CommandText.Contains(".*", StringComparison.Ordinal));
            AssertValidSql(plan.CommandText);
        }

        [TestMethod]
        public void MembershipQuery_NeverSelectsEveryColumn()
        {
            var filters = new[]
            {
                string.Empty,
                "Department = 'Sales'",
                "users.Department = 'Sales' AND agents.BlueprintId = 'alpha'",
            };

            foreach (var managerEmployeeId in new int?[] { null, 42 })
            {
                foreach (var filter in filters)
                {
                    var request = new SqlMembershipEvaluationRequest(
                        managerEmployeeId,
                        depth: 2,
                        filter,
                        IdentityTypeSet.All,
                        tableName: "run123");
                    var compilation = string.IsNullOrEmpty(filter)
                        ? SqlFilterCompilationResult.Valid(Array.Empty<IdentityType>(), Array.Empty<string>())
                        : Analyze(filter);

                    var evaluation = SqlMembershipQueryBuilder.BuildEvaluation(request, compilation);
                    var probe = SqlMembershipQueryBuilder.BuildProbe(request, compilation, IdentityType.Users);

                    Assert.IsFalse(evaluation.CommandText.Contains("*", StringComparison.Ordinal), evaluation.CommandText);
                    Assert.IsFalse(probe.CommandText.Contains("*", StringComparison.Ordinal), probe.CommandText);
                }
            }
        }

        [TestMethod]
        public void MissingEmployeeIdCheck_TestsForNullWithoutConvertingEveryRowToText()
        {
            const string filter = "Department = 'Sales'";
            const string nullCheck = "CASE WHEN [users].[EmployeeId] IS NULL THEN 1 ELSE 0 END";
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter,
                IdentityTypeSet.UsersOnly,
                tableName: "run123");

            var evaluation = SqlMembershipQueryBuilder.BuildEvaluation(request, Analyze(filter));
            var probe = SqlMembershipQueryBuilder.BuildProbe(request, Analyze(filter), IdentityType.Users);

            foreach (var commandText in new[] { evaluation.CommandText, probe.CommandText })
            {
                StringAssert.Contains(commandText, nullCheck);
                Assert.IsFalse(
                    commandText.Contains("CONVERT(nvarchar(4000), [users].[EmployeeId])", StringComparison.Ordinal),
                    commandText);
                AssertValidSql(commandText);
            }
        }

        [TestMethod]
        public void MembershipProbe_AggregatesOneUserAnswerWithoutMaterializingTheSet()
        {
            const string filter = "users.Department = 'Sales'";
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter,
                IdentityTypeSet.UsersOnly,
                tableName: "run123");
            var plan = SqlMembershipQueryBuilder.BuildProbe(
                request,
                Analyze(filter),
                IdentityType.Users);

            Assert.IsFalse(plan.RequiresAgents);
            StringAssert.Contains(plan.CommandText, "AS [Matched]");
            StringAssert.Contains(
                plan.CommandText,
                "TRY_CONVERT(uniqueidentifier, [users].[AzureObjectId]) = @ObjectId");
            StringAssert.Contains(
                plan.CommandText,
                "AS [MalformedUserEmployeeId]");
            Assert.IsFalse(
                plan.CommandText.Contains(
                    "GROUP BY",
                    StringComparison.Ordinal));
            AssertValidSql(plan.CommandText);
        }

        [TestMethod]
        public void MembershipProbe_MatchesOnlyHyphenatedAgentIdentifiers()
        {
            const string filter = "Department = 'Sales'";
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter,
                IdentityTypeSet.Of(IdentityType.Agents),
                tableName: "run123");
            var plan = SqlMembershipQueryBuilder.BuildProbe(
                request,
                Analyze(filter),
                IdentityType.Agents);

            Assert.IsTrue(plan.RequiresAgents);
            StringAssert.Contains(
                plan.CommandText,
                "TRY_CONVERT(uniqueidentifier, [agents].[AgentObjectId]) = @ObjectId");
            StringAssert.Contains(
                plan.CommandText,
                "DATALENGTH(CONVERT(nvarchar(4000), [agents].[AgentObjectId])) = 72");
            StringAssert.Contains(
                plan.CommandText,
                "LOWER([agents].[AgentObjectId]) COLLATE Latin1_General_100_BIN2");
            Assert.IsFalse(
                plan.CommandText.Contains(
                    "MalformedAgentObjectId",
                    StringComparison.Ordinal));
            AssertValidSql(plan.CommandText);
        }

        [TestMethod]
        public void MembershipProbe_DoesNotValidateUnprojectedAgentIdentifiers()
        {
            const string filter = "agents.BlueprintId = 'alpha'";
            var request = new SqlMembershipEvaluationRequest(
                managerEmployeeId: null,
                depth: 0,
                filter,
                IdentityTypeSet.UsersOnly,
                tableName: "run123");
            var plan = SqlMembershipQueryBuilder.BuildProbe(
                request,
                Analyze(filter),
                IdentityType.Users);

            Assert.IsTrue(plan.RequiresAgents);
            Assert.IsFalse(
                plan.CommandText.Contains(
                    "LOWER([agents].[AgentObjectId])",
                    StringComparison.Ordinal));
            AssertValidSql(plan.CommandText);
        }

        private static int CountOccurrences(string text, string value)
        {
            return text.Split(value, StringSplitOptions.None).Length - 1;
        }

        private static void AssertValidSql(string sql)
        {
            var parser = new TSql150Parser(initialQuotedIdentifiers: false);
            using var reader = new StringReader(sql);
            parser.Parse(reader, out IList<ParseError> errors);

            Assert.AreEqual(
                0,
                errors.Count,
                string.Join(
                    Environment.NewLine,
                    errors.Select(error =>
                        $"{error.Line}:{error.Column} {error.Message}")));
        }
    }
}
