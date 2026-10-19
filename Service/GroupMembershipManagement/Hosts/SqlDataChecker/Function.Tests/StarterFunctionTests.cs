// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace SqlDataChecker.Function.Tests
{
    [TestClass]
    public class StarterFunctionTests
    {
        [TestMethod]
        public void Starter_RequiresPostBodyWithValidRunId()
        {
            var source = ReadPublicSource(
                "Service/GroupMembershipManagement/Hosts/SqlDataChecker/Function/Starter/StarterFunction.cs");

            StringAssert.Contains(source, "\"post\"");
            StringAssert.Contains(source, "runId");
            StringAssert.Contains(source, "Guid.TryParse");
            StringAssert.Contains(source, "BadRequest");
            Assert.IsFalse(source.Contains("\"get\"", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Starter_SchedulesOrchestrationWithCurrentRunId()
        {
            var source = ReadPublicSource(
                "Service/GroupMembershipManagement/Hosts/SqlDataChecker/Function/Starter/StarterFunction.cs");

            StringAssert.Contains(source, "ScheduleNewOrchestrationInstanceAsync");
            StringAssert.Contains(source, "RunId");
            Assert.IsFalse(source.Contains(
                "ScheduleNewOrchestrationInstanceAsync(nameof(OrchestratorFunction), null)",
                StringComparison.Ordinal));
        }

        private static string ReadPublicSource(string relativePath)
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Service", "GroupMembershipManagement")))
                {
                    return File.ReadAllText(Path.Combine(
                        directory.FullName,
                        relativePath.Replace('/', Path.DirectorySeparatorChar)));
                }
            }

            Assert.Fail("Could not locate the public repository root.");
            return string.Empty;
        }
    }
}
