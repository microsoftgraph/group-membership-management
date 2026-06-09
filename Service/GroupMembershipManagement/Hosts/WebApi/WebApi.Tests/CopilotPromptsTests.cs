// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.WebApi;

namespace WebApi.Tests
{
    [TestClass]
    public class CopilotPromptsTests
    {
        [TestMethod]
        public void ChatPrompt_IsNotNullOrEmpty()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(CopilotPrompts.ChatPrompt));
        }

        [TestMethod]
        public void ChatPrompt_ContainsAttributePlaceholder()
        {
            // The prompt must have {0} for HR attribute injection
            Assert.IsTrue(CopilotPrompts.ChatPrompt.Contains("{0}"));
        }

        [TestMethod]
        public void ChatPrompt_ContainsGuardrails()
        {
            Assert.IsTrue(CopilotPrompts.ChatPrompt.Contains("GUARDRAILS"));
        }

        [TestMethod]
        public void ChatPrompt_ContainsToolDefinitions()
        {
            Assert.IsTrue(CopilotPrompts.ChatPrompt.Contains("get_attribute_values"));
            Assert.IsTrue(CopilotPrompts.ChatPrompt.Contains("lookup_person"));
            Assert.IsTrue(CopilotPrompts.ChatPrompt.Contains("validate_org_leader"));
        }

        [TestMethod]
        public void ChatPrompt_ContainsJsonOutputFormat()
        {
            Assert.IsTrue(CopilotPrompts.ChatPrompt.Contains("sourceParts"));
            Assert.IsTrue(CopilotPrompts.ChatPrompt.Contains("response"));
        }

        [TestMethod]
        public void ChatPrompt_ContainsOrgStructureInstructions()
        {
            Assert.IsTrue(CopilotPrompts.ChatPrompt.Contains("useOrgStructure"));
            Assert.IsTrue(CopilotPrompts.ChatPrompt.Contains("orgLeaderName"));
            Assert.IsTrue(CopilotPrompts.ChatPrompt.Contains("orgLeaderDepth"));
        }

        [TestMethod]
        public void ChatPrompt_PlaceholderCanBeReplaced()
        {
            var attributes = "- Attr1 (Description 1)\n- Attr2 (Description 2)";
            var result = CopilotPrompts.ChatPrompt.Replace("{0}", attributes);

            Assert.IsTrue(result.Contains("Attr1 (Description 1)"));
            Assert.IsFalse(result.Contains("{0}"));
        }
    }
}
