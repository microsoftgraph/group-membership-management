// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.SqlMembershipRepository;
using System.Collections.Generic;

namespace Services.Tests
{
    internal static class SqlFilterTestHelpers
    {
        public static IReadOnlyList<string> UserColumnNames { get; } = new[]
        {
            "EmployeeId",
            "ManagerId",
            "AccountEnabled",
            "Department",
        };

        public static IReadOnlyList<string> AgentColumnNames { get; } = new[]
        {
            "AgentObjectId",
            "AgentManagerId",
            "BlueprintId",
            "AgentAccountEnabled",
        };

        public static SqlFilterCompilationResult Analyze(string filter)
        {
            var compilation = SqlMembershipRepository.CheckFilter(
                filter,
                "run123",
                UserColumnNames,
                AgentColumnNames);

            Assert.IsTrue(compilation.IsValid, compilation.Error);
            return compilation;
        }
    }
}
