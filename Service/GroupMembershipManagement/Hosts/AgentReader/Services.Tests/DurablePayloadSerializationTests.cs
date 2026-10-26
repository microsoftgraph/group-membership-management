// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AgentReader.Services.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Hosts.AgentReader.Services.Tests
{
    // Payloads recorded by the previous release must still deserialize when an orchestration replays after a deploy.
    [TestClass]
    public class DurablePayloadSerializationTests
    {
        private const string RunId = "11111111-2222-4333-8444-555555555555";

        public static IEnumerable<object[]> SerializerOptions =>
        [
            [new JsonSerializerOptions()],
            [new JsonSerializerOptions(JsonSerializerDefaults.Web)]
        ];

        [DataTestMethod]
        [DynamicData(nameof(SerializerOptions))]
        public void PageRequestFromThePreviousRelease_DoesNotWriteAgentsWithoutManager(JsonSerializerOptions options)
        {
            var request = JsonSerializer.Deserialize<AgentPageRequest>(
                $$"""{"RunId":"{{RunId}}","NextLink":null}""", options);

            Assert.IsNotNull(request);
            Assert.AreEqual(Guid.Parse(RunId), request.RunId);
            Assert.IsFalse(request.WriteAgentsWithoutManager);
        }

        [DataTestMethod]
        [DynamicData(nameof(SerializerOptions))]
        public void PageRequest_RoundTripsWriteAgentsWithoutManager(JsonSerializerOptions options)
        {
            var original = new AgentPageRequest(Guid.Parse(RunId), "https://graph.microsoft.com/v1.0/next", true);

            var copy = JsonSerializer.Deserialize<AgentPageRequest>(JsonSerializer.Serialize(original, options), options);

            Assert.AreEqual(original, copy);
        }

        [DataTestMethod]
        [DynamicData(nameof(SerializerOptions))]
        public void TableInfo_RoundTripsAndAMissingResultIsNull(JsonSerializerOptions options)
        {
            var original = new AgentTableInfo(AgentManagerIdAllowsNull: true);

            Assert.AreEqual(original, JsonSerializer.Deserialize<AgentTableInfo>(JsonSerializer.Serialize(original, options), options));
            Assert.IsNull(JsonSerializer.Deserialize<AgentTableInfo>("null", options));
        }

        [DataTestMethod]
        [DynamicData(nameof(SerializerOptions))]
        public void PageFromThePreviousRelease_HasNoAgentsWrittenWithoutManager(JsonSerializerOptions options)
        {
            var page = JsonSerializer.Deserialize<AgentPage>($$"""
                {
                  "Agents":[{"AgentObjectId":"{{RunId}}","AgentManagerId":7,"BlueprintId":null,"AgentAccountEnabled":true}],
                  "NextLink":null,
                  "Counters":{"TotalEnumerated":2,"Inserted":0,"FilterPassingAgents":1,"FilteredNoManager":1}
                }
                """, options);

            Assert.IsNotNull(page);
            Assert.AreEqual(7, page.Agents[0].AgentManagerId);
            Assert.AreEqual(0L, page.Counters.WrittenWithoutManager);
            Assert.AreEqual(1L, page.Counters.TotalFiltered);
        }
    }
}
