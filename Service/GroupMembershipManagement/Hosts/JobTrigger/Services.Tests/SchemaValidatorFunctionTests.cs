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

        [TestMethod]
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
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\",\"identityTypes\":[\"users\"]}]")]
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
        public async Task IdentityTypes_RejectsNonSqlParts()
        {
            var nonSqlQueries = new[]
            {
                "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\",\"identityTypes\":[\"users\"]}]",
                "[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"],\"identityTypes\":[\"agents\"]}]",
                "[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-0000-0000-000000000000\",\"identityTypes\":[\"users\"]},{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"users\"]}]",
            };

            foreach (var query in nonSqlQueries)
            {
                Assert.IsFalse(
                    await ValidateAsync(query),
                    "identityTypes must remain forbidden on non-SQL parts");
            }
        }

        [TestMethod]
        public async Task IdentityTypes_FailsWhenQuerySchemaIsMissing()
        {
            var selectedQuery = "[{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"agents\"]}]";
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
