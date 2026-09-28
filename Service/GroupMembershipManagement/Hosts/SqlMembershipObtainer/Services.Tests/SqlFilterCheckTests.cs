// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.SqlMembershipRepository;
using System;
using System.Linq;

namespace Services.Tests
{
    [TestClass]
    public class SqlFilterCheckTests
    {
        [DataTestMethod]
        [DataRow("BlueprintId = 'alpha'")]
        [DataRow("agents.AgentManagerId = 5")]
        [DataRow("AgentObjectId IS NOT NULL")]
        [DataRow("agents.AgentAccountEnabled = 1")]
        [DataRow("blueprintid = 'alpha'")]
        [DataRow("AGENTS.BlueprintId = 'alpha'")]
        [DataRow("[agents].[BlueprintId] = 'alpha'")]
        public void AcceptsAgentColumnsWithOrWithoutTheTablePrefix(string filter)
        {
            var result = Check(filter);

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.IsTrue(result.UsesAgents);
            CollectionAssert.AreEqual(
                new[] { IdentityType.Agents },
                result.ReferencedIdentityTypes.ToArray());
        }

        [DataTestMethod]
        [DataRow("Department = 'Sales'")]
        [DataRow("users.Department = 'Sales'")]
        [DataRow("ManagerId = 5 AND AccountEnabled = 1")]
        [DataRow("[Users].[Department] = 'Sales'")]
        public void ReportsUsersForUsersColumns(string filter)
        {
            var result = Check(filter);

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.IsFalse(result.UsesAgents);
            CollectionAssert.AreEqual(
                new[] { IdentityType.Users },
                result.ReferencedIdentityTypes.ToArray());
        }

        [TestMethod]
        public void ReportsBothTablesWhenTheFilterUsesBoth()
        {
            var result = Check(
                "users.Department = 'Sales' AND agents.AgentAccountEnabled = 1");

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.IsTrue(result.UsesAgents);
            CollectionAssert.AreEquivalent(
                new[] { IdentityType.Users, IdentityType.Agents },
                result.ReferencedIdentityTypes.ToArray());
        }

        [TestMethod]
        public void ReportsEachUsersColumnOnceInTheUsersTableSpelling()
        {
            var result = Check(
                "department = 'Sales' AND users.Department <> 'x' AND EMPLOYEEID > 0 AND agents.BlueprintId = 'alpha'");

            Assert.IsTrue(result.IsValid, result.Error);
            CollectionAssert.AreEquivalent(
                new[] { "Department", "EmployeeId" },
                result.ReferencedUserColumnNames.ToArray());
        }

        [TestMethod]
        public void ReportsNoUsersColumnsForAnAgentsOnlyFilter()
        {
            var result = Check("agents.BlueprintId = 'alpha' AND AgentAccountEnabled = 1");

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.AreEqual(0, result.ReferencedUserColumnNames.Count);
        }

        [TestMethod]
        public void ReportsNoTablesForAFilterWithoutColumns()
        {
            var result = Check("1 = 1");

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.IsFalse(result.UsesAgents);
            Assert.AreEqual(0, result.ReferencedIdentityTypes.Count);
            Assert.AreEqual(0, result.ReferencedUserColumnNames.Count);
        }

        [TestMethod]
        public void RejectsAnUnknownColumn()
        {
            var result = Check("Departmnet = 'Sales'");

            Assert.IsFalse(result.IsValid);
            Assert.IsFalse(result.UsesAgents);
            Assert.AreEqual(0, result.ReferencedUserColumnNames.Count);
            Assert.AreEqual(
                "Invalid column name detected: Departmnet",
                result.Error);
        }

        [TestMethod]
        public void CountsAColumnInBothTablesAsAUsersColumn()
        {
            var result = SqlMembershipRepository.CheckFilter(
                "Department = 'Sales'",
                "run123",
                SqlFilterTestHelpers.UserColumnNames,
                SqlFilterTestHelpers.AgentColumnNames.Append("Department").ToArray());

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.IsFalse(result.UsesAgents);
            CollectionAssert.AreEqual(
                new[] { IdentityType.Users },
                result.ReferencedIdentityTypes.ToArray());
            CollectionAssert.AreEqual(
                new[] { "Department" },
                result.ReferencedUserColumnNames.ToArray());
        }

        [DataTestMethod]
        [DataRow("agents.Department = 'Sales'", "Department")]
        [DataRow("[AGENTS].[Department] = 'Sales'", "Department")]
        [DataRow("users.BlueprintId = 'alpha'", "BlueprintId")]
        [DataRow("Department = 'Sales' AND users.AgentAccountEnabled = 1", "AgentAccountEnabled")]
        public void RejectsAColumnThatIsNotInTheTableItsPrefixNames(string filter, string invalidColumnName)
        {
            var result = Check(filter);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(
                $"Invalid column name detected: {invalidColumnName}",
                result.Error);
        }

        [TestMethod]
        public void CountsAnAgentsPrefixedColumnAsAnAgentsColumnEvenWhenTheUsersTableHasTheSameColumn()
        {
            var result = SqlMembershipRepository.CheckFilter(
                "agents.Department = 'Sales'",
                "run123",
                SqlFilterTestHelpers.UserColumnNames,
                SqlFilterTestHelpers.AgentColumnNames.Append("Department").ToArray());

            Assert.IsTrue(result.IsValid, result.Error);
            CollectionAssert.AreEqual(
                new[] { IdentityType.Agents },
                result.ReferencedIdentityTypes.ToArray());
            Assert.AreEqual(0, result.ReferencedUserColumnNames.Count);
        }

        [TestMethod]
        public void RejectsAnUnknownColumnWithoutAPrefixWhenTheAgentsTableHasNoColumns()
        {
            var result = SqlMembershipRepository.CheckFilter(
                "Department = 'Sales' AND Departmnet = 'Sales'",
                "run123",
                SqlFilterTestHelpers.UserColumnNames,
                Array.Empty<string>());

            Assert.IsFalse(result.IsValid);
            Assert.IsFalse(result.UsesAgents);
            Assert.AreEqual(
                "Invalid column name detected: Departmnet",
                result.Error);
        }

        [TestMethod]
        public void CountsAnAgentsPrefixedColumnAsAnAgentsColumnWhenTheAgentsTableHasNoColumns()
        {
            var result = SqlMembershipRepository.CheckFilter(
                "Department = 'Sales' AND agents.BlueprintId = 'alpha'",
                "run123",
                SqlFilterTestHelpers.UserColumnNames,
                Array.Empty<string>());

            Assert.IsTrue(result.IsValid, result.Error);
            Assert.IsTrue(result.UsesAgents);
            CollectionAssert.AreEquivalent(
                new[] { IdentityType.Users, IdentityType.Agents },
                result.ReferencedIdentityTypes.ToArray());
        }

        [TestMethod]
        public void RejectsMultipleStatements()
        {
            var result = Check("Department = 'Sales'; SELECT 1");

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(
                "Multiple SQL statements are not allowed.",
                result.Error);
        }

        [TestMethod]
        public void RejectsAFilterThatClosesTheWhereClauseEarly()
        {
            var result = Check(
                "Department = 'Sales') UNION SELECT * FROM [users].[run123] WHERE (1 = 1");

            Assert.IsFalse(result.IsValid);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error));
        }

        [TestMethod]
        public void RejectsAnEmptyFilter()
        {
            var result = Check(string.Empty);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("Unexpected end of file occurred.", result.Error);
        }

        private static SqlFilterCompilationResult Check(string filter)
        {
            return SqlMembershipRepository.CheckFilter(
                filter,
                "run123",
                SqlFilterTestHelpers.UserColumnNames,
                SqlFilterTestHelpers.AgentColumnNames);
        }
    }
}
