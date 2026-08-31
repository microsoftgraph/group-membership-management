// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Text.Json;
using Services.WebApi;
using Services.WebApi.Contracts;

namespace WebApi.Tests
{
    /// <summary>
    /// Characterization tests pinning the wire shape of the model-emitted operation response
    /// { message, operations[] } for the representative scenarios the Copilot service must handle:
    /// single HR filter, multi-part, group membership, and org-leader. These pin the deserialization
    /// contract that <see cref="CopilotService"/> relies on before/after the strict-schema refactor.
    /// </summary>
    [TestClass]
    public class CopilotServiceCharacterizationTests
    {
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        private static CopilotOperationResponse Parse(string json) =>
            JsonSerializer.Deserialize<CopilotOperationResponse>(json, Options)!;

        [TestMethod]
        public void OperationResponseSchema_IsWellFormedJson()
        {
            // The strict json_schema handed to the model must itself be valid JSON.
            using var doc = JsonDocument.Parse(CopilotPrompts.OperationResponseJsonSchema);
            Assert.AreEqual(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.IsTrue(doc.RootElement.TryGetProperty("properties", out _));
        }

        [TestMethod]
        public void SingleHrFilter_DeserializesToOneAddOperation()
        {
            var json = @"{
                ""message"": ""Added US full-time employees."",
                ""operations"": [
                    { ""op"": ""add"", ""partId"": null, ""part"": { ""sourceType"": ""SqlMembership"", ""filter"": ""Country = 'USA'"", ""title"": ""US"", ""isExclusion"": false, ""useOrgStructure"": false, ""orgLeaderName"": null, ""orgLeaderEmail"": null, ""orgLeaderDepth"": null, ""groupId"": null, ""groupName"": null }, ""parts"": null }
                ]
            }";

            var parsed = Parse(json);

            Assert.AreEqual("Added US full-time employees.", parsed.Message);
            Assert.AreEqual(1, parsed.Operations.Count);
            Assert.AreEqual("add", parsed.Operations[0].Op);
            Assert.IsNull(parsed.Operations[0].PartId);
            Assert.AreEqual("Country = 'USA'", parsed.Operations[0].Part!.Filter);
            Assert.AreEqual("SqlMembership", parsed.Operations[0].Part!.SourceType);
        }

        [TestMethod]
        public void MultiPart_Set_DeserializesToOneSetWithAllParts()
        {
            var json = @"{
                ""message"": ""Rebuilt the query."",
                ""operations"": [
                    { ""op"": ""set"", ""partId"": null, ""part"": null, ""parts"": [
                        { ""sourceType"": ""SqlMembership"", ""filter"": ""JobType = 'FT'"", ""title"": ""FT"", ""isExclusion"": false, ""useOrgStructure"": false, ""orgLeaderName"": null, ""orgLeaderEmail"": null, ""orgLeaderDepth"": null, ""groupId"": null, ""groupName"": null },
                        { ""sourceType"": ""GroupMembership"", ""filter"": null, ""title"": ""Leads"", ""isExclusion"": false, ""useOrgStructure"": false, ""orgLeaderName"": null, ""orgLeaderEmail"": null, ""orgLeaderDepth"": null, ""groupId"": ""g-1"", ""groupName"": ""Leads"" }
                    ] }
                ]
            }";

            var parsed = Parse(json);

            Assert.AreEqual(1, parsed.Operations.Count);
            Assert.AreEqual("set", parsed.Operations[0].Op);
            Assert.AreEqual(2, parsed.Operations[0].Parts!.Count);
            Assert.AreEqual("GroupMembership", parsed.Operations[0].Parts![1].SourceType);
        }

        [TestMethod]
        public void GroupMembership_Add_CarriesGroupIdAndName()
        {
            var json = @"{
                ""message"": ""Added the Seattle Leads group."",
                ""operations"": [
                    { ""op"": ""add"", ""partId"": null, ""part"": { ""sourceType"": ""GroupMembership"", ""filter"": null, ""title"": ""Seattle Leads"", ""isExclusion"": false, ""useOrgStructure"": false, ""orgLeaderName"": null, ""orgLeaderEmail"": null, ""orgLeaderDepth"": null, ""groupId"": ""grp-guid"", ""groupName"": ""Seattle Leads"" }, ""parts"": null }
                ]
            }";

            var parsed = Parse(json);
            var part = parsed.Operations[0].Part!;

            Assert.AreEqual("GroupMembership", part.SourceType);
            Assert.AreEqual("grp-guid", part.GroupId);
            Assert.AreEqual("Seattle Leads", part.GroupName);
        }

        [TestMethod]
        public void OrgLeader_Add_CarriesOrgLeaderFields()
        {
            var json = @"{
                ""message"": ""Scoped to John's org."",
                ""operations"": [
                    { ""op"": ""add"", ""partId"": null, ""part"": { ""sourceType"": ""SqlMembership"", ""filter"": null, ""title"": ""John's Org"", ""isExclusion"": false, ""useOrgStructure"": true, ""orgLeaderName"": ""John Smith"", ""orgLeaderEmail"": ""john.smith@contoso.com"", ""orgLeaderDepth"": 2, ""groupId"": null, ""groupName"": null }, ""parts"": null }
                ]
            }";

            var parsed = Parse(json);
            var part = parsed.Operations[0].Part!;

            Assert.IsTrue(part.UseOrgStructure);
            Assert.AreEqual("John Smith", part.OrgLeaderName);
            Assert.AreEqual("john.smith@contoso.com", part.OrgLeaderEmail);
            Assert.AreEqual(2, part.OrgLeaderDepth);
        }

        [TestMethod]
        public void ClarifyingQuestion_HasMessageAndNoOperations()
        {
            var json = @"{ ""message"": ""Company-wide or within an org?"", ""operations"": [] }";

            var parsed = Parse(json);

            Assert.AreEqual("Company-wide or within an org?", parsed.Message);
            Assert.AreEqual(0, parsed.Operations.Count);
        }
    }
}
