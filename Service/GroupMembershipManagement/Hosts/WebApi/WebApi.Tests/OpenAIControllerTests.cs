// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Configuration;
using Moq;
using System.Reflection;
using WebApi.Controllers.v1.OpenAI;
using WebApi.BackgroundServices;

namespace Services.Tests
{
    [TestClass]
    public class OpenAIControllerTests
    {
        private Mock<OpenAIService> _mockOpenAIService = null!;
        private OpenAIController _controller = null!;

        [TestInitialize]
        public void Initialize()
        {
            var mockConfiguration = new Mock<IConfiguration>();
            mockConfiguration.Setup(x => x["Settings:OpenAIEndpoint"]).Returns("https://test-endpoint.com");

            _mockOpenAIService = new Mock<OpenAIService>(mockConfiguration.Object);
            _controller = new OpenAIController(_mockOpenAIService.Object);
        }

        [TestMethod]
        public void Constructor_WithNullService_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new OpenAIController(null!));
        }

        [TestMethod]
        public void BuildTitlePrompt_WithValidFilter_ReturnsNonEmptyPrompt()
        {
            var filter = "EmployeeType_Code = 'FTE' And LocationArea_Code = 'MX'";
            var prompt = InvokeBuildTitlePrompt(filter);

            Assert.IsFalse(string.IsNullOrEmpty(prompt), "Prompt should not be empty");
            Assert.IsTrue(prompt.Contains($"Here is the filter: {filter}"), "Prompt should contain the filter");
            Assert.IsTrue(prompt.Contains("Create **one** string title"), "Prompt should contain instruction");
            Assert.IsTrue(prompt.Contains("SQL WHERE clause format"), "Prompt should mention SQL format");
            Assert.IsTrue(prompt.Contains("Don't include any prefixes"), "Prompt should contain prefix instruction");
        }

        [TestMethod]
        public void BuildTitlePrompt_WithEmptyFilter_ReturnsNonEmptyPrompt()
        {
            var filter = "";
            var prompt = InvokeBuildTitlePrompt(filter);

            Assert.IsFalse(string.IsNullOrEmpty(prompt), "Prompt should not be empty even with empty filter");
            Assert.IsTrue(prompt.Contains("Here is the filter: "), "Prompt should contain filter placeholder");
            Assert.IsTrue(prompt.Length > 100, "Prompt should have substantial content beyond just the filter");
        }

        [TestMethod]
        public void BuildTitlePrompt_WithNullFilter_ReturnsNonEmptyPrompt()
        {
            string? filter = null;
            var prompt = InvokeBuildTitlePrompt(filter);

            Assert.IsFalse(string.IsNullOrEmpty(prompt), "Prompt should not be empty even with null filter");
            Assert.IsTrue(prompt.Contains("Here is the filter: "), "Prompt should contain filter placeholder");
            Assert.IsTrue(prompt.Length > 100, "Prompt should have substantial content");
        }

        [TestMethod]
        public void BuildTitlePrompt_WithComplexFilter_ContainsFullFilter()
        {
            var filter = "Department = 'Engineering' AND (JobFunction_Code = 'N1F' OR JobFunction_Code = 'N1J') AND EmployeeType_Code IN ('FTE', 'Vendor')";
            var prompt = InvokeBuildTitlePrompt(filter);

            Assert.IsFalse(string.IsNullOrEmpty(prompt), "Prompt should not be empty");
            Assert.IsTrue(prompt.Contains(filter), "Prompt should contain the complete complex filter");
            Assert.IsTrue(prompt.Contains("AND/OR"), "Prompt should mention AND/OR operators");
            Assert.IsTrue(prompt.Contains("IN, NOT IN"), "Prompt should mention IN operator");
        }

        [TestMethod]
        public void BuildTitlePrompt_ContainsAllRequiredInstructions()
        {
            var filter = "Simple = 'Test'";
            var prompt = InvokeBuildTitlePrompt(filter);

            Assert.IsFalse(string.IsNullOrEmpty(prompt), "Prompt should not be empty");
            Assert.IsTrue(prompt.Contains("Create **one** string title"), "Should instruct to create one title");
            Assert.IsTrue(prompt.Contains("SQL WHERE clause format"), "Should mention SQL format");
            Assert.IsTrue(prompt.Contains("[ATTRIBUTE] [OPERATOR] [VALUE]"), "Should show format example");
            Assert.IsTrue(prompt.Contains("CountryName = 'USA' AND ChildCount > 2"), "Should provide example");
            Assert.IsTrue(prompt.Contains("=, <>, >, <, >=, <=, IN, NOT IN"), "Should list operators");
            Assert.IsTrue(prompt.Contains("Don't include any prefixes"), "Should instruct about prefixes");
            Assert.IsTrue(prompt.Contains("Do not generate multiple titles"), "Should instruct single title");
        }

        [TestMethod]
        public void BuildTitlePrompt_WithSpecialCharacters_HandlesCorrectly()
        {
            var filter = "Email LIKE '%@microsoft.com%'";
            var prompt = InvokeBuildTitlePrompt(filter);

            Assert.IsFalse(string.IsNullOrEmpty(prompt), "Prompt should not be empty");
            Assert.IsTrue(prompt.Contains(filter), "Prompt should contain filter with special characters");
        }

        [TestMethod]
        public void BuildTitlePrompt_WithLongFilter_HandlesCorrectly()
        {
            var filter = "Department = 'Engineering' AND Location IN ('Seattle', 'Redmond', 'Bellevue', 'Kirkland') " +
                        "AND (StartDate >= '2020-01-01' AND StartDate <= '2023-12-31') " +
                        "AND (Title LIKE '%Senior%' OR Title LIKE '%Principal%' OR Title LIKE '%Manager%') " +
                        "AND Status = 'Active' AND EmployeeType <> 'Contractor'";

            var prompt = InvokeBuildTitlePrompt(filter);
            Assert.IsFalse(string.IsNullOrEmpty(prompt), "Prompt should not be empty");
            Assert.IsTrue(prompt.Contains(filter), "Prompt should contain the complete long filter");
            Assert.IsTrue(prompt.Length > filter.Length + 100, "Prompt should have additional instructional content");
        }

        [TestMethod]
        public void BuildTitlePrompt_AlwaysReturnsConsistentStructure()
        {
            var filters = new string?[]
            {
                "Status = 'Active'",
                "",
                null,
                "Complex = 'Filter' AND Multiple = 'Conditions'",
                "Special='Chars&Symbols%'"
            };

            foreach (var filter in filters)
            {
                var prompt = InvokeBuildTitlePrompt(filter);

                Assert.IsFalse(string.IsNullOrEmpty(prompt), $"Prompt should not be empty for filter: {filter ?? "null"}");
                Assert.IsTrue(prompt.Contains("Create **one** string title"), "Prompt should always contain main instruction");
                Assert.IsTrue(prompt.Contains("Here is the filter:"), "Prompt should always contain filter section");
                Assert.IsTrue(prompt.Contains("SQL WHERE clause format"), "Prompt should always explain format");
            }
        }

        private string InvokeBuildTitlePrompt(string? filter)
        {
            var method = typeof(OpenAIController).GetMethod("BuildTitlePrompt",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(method, "BuildTitlePrompt method should exist");

            var result = method.Invoke(_controller, new object?[] { filter });
            return result as string ?? string.Empty;
        }
    }
}