// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.JobTrigger;
using JobTrigger.Activity.SchemaValidator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Services.Contracts;
using Services.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SchemaValidatorFunctionTests
    {
        private Mock<IJobTriggerService> _jobTriggerService;
        private JsonSchemaProvider _jsonSchemaProvider;

        [TestInitialize]
        public void Setup()
        {
            _jobTriggerService = new Mock<IJobTriggerService>();
            _jsonSchemaProvider = SchemaProviderFactory.CreateJsonSchemaProvider();
        }

        [DataTestMethod]
        public async Task TestValidQueriesAsync()
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, _jobTriggerService.Object, _jsonSchemaProvider);

            var syncJob = new SyncJob
            {
                RunId = Guid.Empty
            };

            foreach (var query in GetValidQueries())
            {
                syncJob.Query = query;
                var isValidJson = await schemaValidatorFunction.ValidateSchemasAsync(syncJob);
                Assert.IsTrue(isValidJson);
            }
        }

        [TestMethod]
        public async Task TestInvalidQueriesAsync()
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, _jobTriggerService.Object, _jsonSchemaProvider);

            var syncJob = new SyncJob
            {
                RunId = Guid.Empty
            };

            foreach (var query in GetInvalidQueries())
            {
                syncJob.Query = query;
                var isValidJson = await schemaValidatorFunction.ValidateSchemasAsync(syncJob);
                Assert.IsFalse(isValidJson);
            }
        }

        [TestMethod]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"}}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"agents\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"users\",\"agents\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"agents\"]},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]")]
        public async Task TestValidSqlIdentityTypesQueriesAsync(string query)
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, _jobTriggerService.Object, _jsonSchemaProvider);
            var syncJob = new SyncJob { RunId = Guid.Empty, Query = query };

            Assert.IsTrue(await schemaValidatorFunction.ValidateSchemasAsync(syncJob));
        }

        [TestMethod]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"devices\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"users\",\"users\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":\"users\"}]")]
        public async Task TestInvalidSqlIdentityTypesQueriesAsync(string query)
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, _jobTriggerService.Object, _jsonSchemaProvider);
            var syncJob = new SyncJob { RunId = Guid.Empty, Query = query };

            Assert.IsFalse(await schemaValidatorFunction.ValidateSchemasAsync(syncJob));
        }

        public List<string> GetValidQueries()
        {
            return new List<string>
            {
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]" },
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]" },
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\",\"exclusionary\":true}]" },
                { "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]}]" },
                { "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]},{\"type\":\"GroupOwnership\",\"source\":[\"00000000-0000-0000-0000-000000000000\"]}]" },
                { "[{\"type\":\"TeamsChannelMembership\",\"source\":{\"group\":\"00000000-0000-0000-0000-000000000000\",\"channel\":\"0:00000000000000000000000000000000@thread.tacv2\"}}]" }
            };
        }

        public List<string> GetInvalidQueries()
        {
            return new List<string>
            {
                { "[{\"types\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]" },
                { "[{\"type\":\"GroupMembership\",\"source\":123,{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]" },
                { "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\",\"exclusionary\":\"abc\"}]" },
                { "[{\"type\":123,\"source\":[\"GroupMembership\"]}]" },
                { "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"]},{\"type\":\"GroupOwnership\",\"source\":[\"TeamsChannelMembership\"],\"test\":123}]" },
            };
        }

        [TestMethod]
        public async Task IdentityTypes_AcceptsSqlSelections()
        {
            foreach (var selection in new[] { "[\"users\"]", "[\"agents\"]", "[\"users\",\"agents\"]", "[\"agents\",\"users\"]" })
            {
                var query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":" + selection + "}]";

                Assert.IsTrue(
                    await ValidateAsync(query),
                    $"expected {selection} to be accepted on a SQL part");
            }
        }

        [TestMethod]
        public async Task IdentityTypes_RejectsInvalidSelections()
        {
            var invalidSelections = new[]
            {
                "[]",
                "null",
                "\"users\"",
                "[\"users\",\"users\"]",
                "[\"Users\"]",
                "[\"devices\"]",
                "[1,2]",
            };

            foreach (var selection in invalidSelections)
            {
                var query = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":" + selection + "}]";

                Assert.IsFalse(
                    await ValidateAsync(query),
                    $"expected {selection} to be rejected");
            }
        }

        [TestMethod]
        public async Task IdentityTypes_AcceptsGroupSelections()
        {
            foreach (var selection in new[] { "[\"users\"]", "[\"agents\"]", "[\"users\",\"agents\"]", "[\"agents\",\"users\"]" })
            {
                var query = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":" + selection + "}]";

                Assert.IsTrue(
                    await ValidateAsync(query),
                    $"expected {selection} to be accepted on a single-source group part");
            }
        }

        [TestMethod]
        public async Task IdentityTypes_RejectsInvalidGroupSelections()
        {
            var invalidSelections = new[]
            {
                "[]",
                "null",
                "\"users\"",
                "[\"users\",\"users\"]",
                "[\"Users\"]",
                "[\"devices\"]",
                "[\"alternates\"]",
                "[1]",
            };

            foreach (var selection in invalidSelections)
            {
                var query = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":" + selection + "}]";

                Assert.IsFalse(
                    await ValidateAsync(query),
                    $"expected {selection} to be rejected on a group part");
            }
        }

        [TestMethod]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"IdentityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":{\"identityTypes\":[\"agents\"]}}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"not-a-guid\",\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-00000000000A\",\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\\n\",\"identityTypes\":[\"users\"]}]")]
        public async Task IdentityTypes_RejectsInvalidSelectedGroupSources(string query)
        {
            Assert.IsFalse(await ValidateAsync(query));
        }

        [TestMethod]
        public async Task IdentityTypes_RejectsUnsupportedParts()
        {
            var unsupportedQueries = new[]
            {
                "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"],\"identityTypes\":[\"agents\"]}]",
                "[{\"type\":\"PlaceMembership\",\"source\":\"building-1\",\"identityTypes\":[\"users\"]}]",
                "[{\"type\":\"TeamsChannelMembership\",\"source\":{\"objectId\":\"team-1\",\"channelId\":\"channel-1\"},\"identityTypes\":[\"users\"]}]",
            };

            foreach (var query in unsupportedQueries)
            {
                Assert.IsFalse(
                    await ValidateAsync(query),
                    "identityTypes must remain forbidden on unsupported parts");
            }
        }

        [TestMethod]
        public async Task IdentityTypes_AcceptsMixedGroupAndSqlSelections()
        {
            const string query =
                "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\"]}," +
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"agents\"]}]";

            Assert.IsTrue(await ValidateAsync(query));
        }

        [TestMethod]
        public async Task IdentityTypes_ValidationPreservesTheOriginalQuery()
        {
            const string query =
                "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"agents\",\"users\"],\"exclusionary\":true}," +
                "{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"agents\"]}]";
            var syncJob = new SyncJob
            {
                RunId = Guid.Empty,
                Query = query,
            };
            var schemaValidatorFunction = new SchemaValidatorFunction(
                NullLogger<SchemaValidatorFunction>.Instance,
                _jobTriggerService.Object,
                _jsonSchemaProvider);

            Assert.IsTrue(await schemaValidatorFunction.ValidateSchemasAsync(syncJob));
            Assert.AreEqual(query, syncJob.Query);
        }

        [TestMethod]
        public async Task IdentityTypes_RejectsSelectionOnLegacyArrayGroupSource()
        {
            const string query =
                "[{\"type\":\"GroupMembership\",\"source\":[\"00000000-0000-0000-0000-000000000000\"],\"identityTypes\":[\"users\"]}]";

            Assert.IsFalse(await ValidateAsync(query));
        }

        [TestMethod]
        public async Task IdentityTypes_LegacyArrayGroupSourceWithoutSelectionRemainsValid()
        {
            const string query =
                "[{\"type\":\"GroupMembership\",\"source\":[\"00000000-0000-0000-0000-000000000000\"]}]";

            Assert.IsTrue(await ValidateAsync(query));
        }

        [TestMethod]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"agents\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"agents\"]}]")]
        public async Task IdentityTypes_FailsWhenQuerySchemaIsMissing(string selectedQuery)
        {
            var legacyQuery = "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\"}]";

            var emptyProvider = new JsonSchemaProvider();
            Assert.IsFalse(await ValidateAsync(selectedQuery, emptyProvider));
            Assert.IsTrue(await ValidateAsync(legacyQuery, emptyProvider));

            var providerWithoutQuerySchema = new JsonSchemaProvider();
            providerWithoutQuerySchema.Schemas.Add(
                "UnrelatedSchema",
                "{\"$schema\":\"http://json-schema.org/draft-04/schema#\",\"type\":\"object\"}");

            Assert.IsFalse(
                await ValidateAsync(selectedQuery, providerWithoutQuerySchema));
            Assert.IsTrue(
                await ValidateAsync(legacyQuery, providerWithoutQuerySchema));
        }

        [TestMethod]
        public async Task IdentityTypes_RejectsInvalidGroupSourceIdentifier()
        {
            const string query =
                "[{\"type\":\"GroupMembership\",\"source\":\"not-a-guid\",\"identityTypes\":[\"users\"]}]";

            Assert.IsFalse(await ValidateAsync(query));
        }

        [TestMethod]
        public async Task IdentityTypes_FindsQuerySchemaRegardlessOfKeyCase()
        {
            var selectedQuery = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"agents\"]}]";

            var providerWithLowercaseKey = new JsonSchemaProvider();
            providerWithLowercaseKey.Schemas.Add("query", _jsonSchemaProvider.Schemas["Query"]);

            Assert.IsTrue(await ValidateAsync(selectedQuery, providerWithLowercaseKey));
        }

        [TestMethod]
        public async Task IdentityTypes_LoadedSchemaStillAcceptsLegacySqlParts()
        {
            var legacyQueries = new[]
            {
                "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}}}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1,\"depth\":2}},\"exclusionary\":true}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"EmployeeType_Code = 'FTE'\"}}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}}},{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000001\"}]",
            };

            foreach (var query in legacyQueries)
            {
                Assert.IsTrue(
                    await ValidateAsync(query),
                    $"legacy SQL query must remain valid: {query}");
            }
        }

        [TestMethod]
        public async Task IdentityTypes_RejectsMisplacedSelection()
        {
            var misplacedQueries = new[]
            {
                "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1},\"identityTypes\":[\"agents\"]}}]",
                "[{\"type\":\"SqlMembership\",\"identityTypes\":[\"users\"],\"source\":{\"manager\":{\"id\":1},\"identityTypes\":[\"agents\"]}}]",
                "[{\"type\":\"GroupMembership\",\"source\":{\"value\":\"00000000-0000-0000-0000-000000000000\",\"identityTypes\":[\"agents\"]}}]",
                "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\"],\"identityTypes\":[\"agents\"]}]",
            };

            foreach (var query in misplacedQueries)
            {
                Assert.IsFalse(
                    await ValidateAsync(query),
                    $"nested identityTypes must be rejected: {query}");
            }
        }

        [TestMethod]
        public async Task IdentityTypes_RejectsKeyWithDifferentLetterCase()
        {
            var wrongCaseQueries = new[]
            {
                "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"IdentityTypes\":[\"agents\"]}]",
                "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1},\"IDENTITYTYPES\":[\"agents\"]}}]",
            };

            foreach (var query in wrongCaseQueries)
            {
                Assert.IsFalse(
                    await ValidateAsync(query),
                    $"a differently cased key must be rejected with the schema loaded: {query}");
                Assert.IsFalse(
                    await ValidateAsync(query, new JsonSchemaProvider()),
                    $"a differently cased key must be rejected when the schema is missing: {query}");
            }
        }

        private async Task<bool> ValidateAsync(
            string query,
            JsonSchemaProvider? schemaProvider = null)
        {
            var schemaValidatorFunction = new SchemaValidatorFunction(
                NullLogger<SchemaValidatorFunction>.Instance,
                _jobTriggerService.Object,
                schemaProvider ?? _jsonSchemaProvider);

            return await schemaValidatorFunction.ValidateSchemasAsync(
                new SyncJob
                {
                    RunId = Guid.Empty,
                    Query = query,
                });
        }
    }
}
