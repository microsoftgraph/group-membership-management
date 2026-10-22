// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core.Serialization;
using Hosts.GroupMembershipObtainer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Tests.Services
{
    [TestClass]
    public sealed class SelectionOutcomeTests
    {
        public static IEnumerable<object[]> AllOutcomes => new[]
        {
            new object[] { SelectionOutcome.Completed() },
            new object[] { SelectionOutcome.RetriesExhausted("lookup still failed after retries (429)") },
            new object[] { SelectionOutcome.LookupUnavailable(403) },
            new object[] { SelectionOutcome.Failed("identification failed") },
        };

        [TestMethod]
        public void Completed_CarriesNoDetail()
        {
            var outcome = SelectionOutcome.Completed();

            Assert.AreEqual(SelectionOutcomeStatus.Completed, outcome.Status);
            Assert.IsNull(outcome.HttpStatus);
            Assert.IsNull(outcome.Reason);
        }

        [TestMethod]
        public void RetriesExhausted_CarriesOnlyReason()
        {
            var outcome = SelectionOutcome.RetriesExhausted("lookup still failed after retries (503)");

            Assert.AreEqual(SelectionOutcomeStatus.RetriesExhausted, outcome.Status);
            Assert.AreEqual("lookup still failed after retries (503)", outcome.Reason);
            Assert.IsNull(outcome.HttpStatus);
        }

        [TestMethod]
        public void LookupUnavailable_CarriesOnlyHttpStatus()
        {
            var outcome = SelectionOutcome.LookupUnavailable(401);

            Assert.AreEqual(SelectionOutcomeStatus.LookupUnavailable, outcome.Status);
            Assert.AreEqual(401, outcome.HttpStatus);
            Assert.IsNull(outcome.Reason);
        }

        [TestMethod]
        public void Failed_CarriesOnlyReason()
        {
            var outcome = SelectionOutcome.Failed("unreadable body");

            Assert.AreEqual(SelectionOutcomeStatus.Failed, outcome.Status);
            Assert.AreEqual("unreadable body", outcome.Reason);
            Assert.IsNull(outcome.HttpStatus);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Failed_WithoutReason_IsRejected(string reason)
        {
            Assert.ThrowsException<ArgumentException>(() => SelectionOutcome.Failed(reason));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void RetriesExhausted_WithoutReason_IsRejected(string reason)
        {
            Assert.ThrowsException<ArgumentException>(() => SelectionOutcome.RetriesExhausted(reason));
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(99)]
        [DataRow(600)]
        public void LookupUnavailable_WithInvalidHttpStatus_IsRejected(int httpStatus)
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => SelectionOutcome.LookupUnavailable(httpStatus));
        }

        [DataTestMethod]
        [DynamicData(nameof(AllOutcomes))]
        public void DefaultJson_RoundTripsEqualOutcome(SelectionOutcome outcome)
        {
            var json = JsonSerializer.Serialize(outcome);
            var roundTripped = JsonSerializer.Deserialize<SelectionOutcome>(json);

            Assert.AreEqual(outcome, roundTripped, json);
        }

        [DataTestMethod]
        [DynamicData(nameof(AllOutcomes))]
        public void WebJson_RoundTripsEqualOutcome(SelectionOutcome outcome)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var json = JsonSerializer.Serialize(outcome, options);
            var roundTripped = JsonSerializer.Deserialize<SelectionOutcome>(json, options);

            Assert.AreEqual(outcome, roundTripped, json);
        }

        [DataTestMethod]
        [DynamicData(nameof(AllOutcomes))]
        public void DurableDefaultSerializer_RoundTripsEqualOutcome(SelectionOutcome outcome)
        {
            var serializer = new JsonObjectSerializer();
            var data = serializer.Serialize(outcome);
            var roundTripped = data.ToObjectFromJson<SelectionOutcome>();

            Assert.AreEqual(outcome, roundTripped, data.ToString());
        }

        [TestMethod]
        public void Json_WritesStatusAsNameAndOmitsAbsentDetail()
        {
            var json = JsonSerializer.Serialize(SelectionOutcome.RetriesExhausted("exhausted"));

            StringAssert.Contains(json, "\"Status\":\"RetriesExhausted\"");
            StringAssert.Contains(json, "\"Reason\":\"exhausted\"");
            Assert.IsFalse(json.Contains("HttpStatus"), json);
        }

        [DataTestMethod]
        [DataRow("{\"Status\":\"RetriesExhausted\"}")]
        [DataRow("{\"Status\":\"Completed\",\"Reason\":\"x\"}")]
        [DataRow("{\"Status\":\"LookupUnavailable\"}")]
        [DataRow("{\"Status\":\"Failed\",\"HttpStatus\":403}")]
        [DataRow("{\"Status\":\"Failed\"}")]
        [DataRow("{\"Status\":\"RetriesExhausted\",\"Reason\":\"x\",\"HttpStatus\":429}")]
        public void Json_InvalidCombination_IsRejectedOnDeserialization(string json)
        {
            Assert.ThrowsException<ArgumentException>(() => JsonSerializer.Deserialize<SelectionOutcome>(json), json);
        }

        [TestMethod]
        public void Json_UndefinedNumericStatus_IsRejected()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => JsonSerializer.Deserialize<SelectionOutcome>("{\"Status\":7}"));
        }

        [DataTestMethod]
        [DataRow("{\"Status\":\"Pending\"}")]
        [DataRow("{\"Status\":\"Throttled\",\"RetryAtUtc\":\"2026-10-20T12:15:00+00:00\"}")]
        public void Json_UnknownStatusName_IsRejected(string json)
        {
            Assert.ThrowsException<JsonException>(() => JsonSerializer.Deserialize<SelectionOutcome>(json), json);
        }
    }
}
