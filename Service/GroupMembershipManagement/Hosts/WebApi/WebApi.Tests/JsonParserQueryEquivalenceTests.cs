// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Services.Helpers;

namespace WebApi.Tests
{
    [TestClass]
    public class JsonParserQueryEquivalenceTests
    {
        [TestMethod]
        public void CompareQueries_WhitespaceAndIndentationOnly_ReturnsEqual()
        {
            var stored = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]";
            var submitted = "[\n  {\n    \"type\": \"GroupMembership\",\n    \"source\": \"00000000-0000-0000-0000-000000000001\"\n  }\n]";

            Assert.AreEqual(JsonParser.QueryEquivalence.Equal, JsonParser.CompareQueries(stored, submitted));
        }

        [TestMethod]
        public void CompareQueries_ObjectKeyOrderDiffers_ReturnsEqual()
        {
            var stored = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]";
            var submitted = "[{\"source\":\"00000000-0000-0000-0000-000000000001\",\"type\":\"GroupMembership\"}]";

            Assert.AreEqual(JsonParser.QueryEquivalence.Equal, JsonParser.CompareQueries(stored, submitted));
        }

        [TestMethod]
        public void CompareQueries_ValueDiffers_ReturnsDiffers()
        {
            var stored = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]";
            var submitted = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000002\"}]";

            Assert.AreEqual(JsonParser.QueryEquivalence.Differs, JsonParser.CompareQueries(stored, submitted));
        }

        [TestMethod]
        public void CompareQueries_ArrayElementOrderDiffers_ReturnsDiffers()
        {
            // Array order is significant: reordering source parts is a real query change.
            var stored = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000002\"}]";
            var submitted = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000002\"},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]";

            Assert.AreEqual(JsonParser.QueryEquivalence.Differs, JsonParser.CompareQueries(stored, submitted));
        }

        [TestMethod]
        public void CompareQueries_UnparseableStoredQuery_ReturnsUnparseable()
        {
            var stored = "not-json";
            var submitted = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]";

            Assert.AreEqual(JsonParser.QueryEquivalence.Unparseable, JsonParser.CompareQueries(stored, submitted));
        }

        [TestMethod]
        public void CompareQueries_UnparseableSubmittedQuery_ReturnsUnparseable()
        {
            var stored = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]";
            var submitted = "{not valid";

            Assert.AreEqual(JsonParser.QueryEquivalence.Unparseable, JsonParser.CompareQueries(stored, submitted));
        }

        [TestMethod]
        public void CompareQueries_NullOrEmptyQuery_ReturnsUnparseable()
        {
            var valid = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]";

            Assert.AreEqual(JsonParser.QueryEquivalence.Unparseable, JsonParser.CompareQueries(null, valid));
            Assert.AreEqual(JsonParser.QueryEquivalence.Unparseable, JsonParser.CompareQueries(valid, null));
            Assert.AreEqual(JsonParser.QueryEquivalence.Unparseable, JsonParser.CompareQueries("   ", valid));
        }
    }
}
