// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Repositories.Contracts.Tests
{
    [TestClass]
    public class RepositoriesContractsTests
    {
        [TestMethod]
        public void VerifyReferenceToEntities()
        {
            var projectPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../Repositories.Contracts", "Repositories.Contracts.csproj");
            var codeFiles = Directory.GetFiles(Path.GetDirectoryName(projectPath), "*.cs", SearchOption.AllDirectories);

            foreach (var classFile in codeFiles)
            {
                var content = File.ReadAllText(classFile);
                Assert.IsFalse(ContainsEntitiesNamespace(content), $"Non-base class {classFile} does not contain a reference to 'Entities'.");
            }
        }

        private bool ContainsEntitiesNamespace(string fileContent)
        {
            var pattern = @"\busing\s+Entities;\s*";
            return Regex.IsMatch(fileContent, pattern);
        }

    }
}