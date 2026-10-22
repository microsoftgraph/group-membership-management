// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupMembershipObtainer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using System.Linq;
using System.Threading.Tasks;
using Tests.Helpers;
using Tests.Services.Infrastructure;

namespace Tests.Services
{
    [TestClass]
    public class SchemaValidatorFunctionTests
    {
        private SchemaValidatorFunction _schemaValidator = null!;

        [TestInitialize]
        public void Setup()
        {
            _schemaValidator = new SchemaValidatorFunction(
                NullLogger<SchemaValidatorFunction>.Instance,
                SchemaProviderFactory.CreateJsonSchemaProvider());
        }

        [TestMethod]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\"}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"agents\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"agents\",\"users\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\",\"agents\"]}")]
        public async Task ValidateSchemasAsync_AcceptsCurrentGroupShapes(string queryPart)
        {
            Assert.IsTrue(await ValidateAsync(queryPart));
        }

        [TestMethod]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":[\"00000000-0000-4000-8000-000000000001\"],\"identityTypes\":[\"agents\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"IdentityTypes\":[\"agents\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":{\"identityTypes\":[\"agents\"]}}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"Users\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":null}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":\"users\"}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[1]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"devices\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"alternates\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\",\"users\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\"],\"identityTypes\":[\"agents\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"not-a-guid\",\"identityTypes\":[\"users\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-00000000000A\",\"identityTypes\":[\"users\"]}")]
        [DataRow("{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\\n\",\"identityTypes\":[\"users\"]}")]
        public async Task ValidateSchemasAsync_RejectsInvalidOrMisplacedSelections(string queryPart)
        {
            Assert.IsFalse(await ValidateAsync(queryPart));
        }

        [TestMethod]
        public async Task ValidateSchemasAsync_RejectsSelectionWhenSchemaIsUnavailable()
        {
            var validatorWithoutSchemas = new SchemaValidatorFunction(
                NullLogger<SchemaValidatorFunction>.Instance,
                new SchemaProvider());

            var result = await validatorWithoutSchemas.ValidateSchemasAsync(CreateRequest(
                "{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"agents\"]}"));

            Assert.IsFalse(result);
        }

        [TestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task ValidateSchemasAsync_MissingSchema_FailsOnlyExplicitSelection(
            bool keepOtherSchemas,
            bool hasExplicitSelection)
        {
            var schemaProvider = keepOtherSchemas
                ? SchemaProviderFactory.CreateMissingGroupMembershipSchemaProvider()
                : new SchemaProvider();
            var validator = new SchemaValidatorFunction(
                NullLogger<SchemaValidatorFunction>.Instance,
                schemaProvider);
            var identitySelection = hasExplicitSelection
                ? ",\"identityTypes\":[\"agents\"]"
                : string.Empty;

            var result = await validator.ValidateSchemasAsync(CreateRequest(
                $"{{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\"{identitySelection}}}"));

            Assert.AreEqual(!hasExplicitSelection, result);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ValidateSchemasAsync_MissingSchemaWithSelection_LogsRejectionReason(
            bool keepOtherSchemas)
        {
            var schemaProvider = keepOtherSchemas
                ? SchemaProviderFactory.CreateMissingGroupMembershipSchemaProvider()
                : new SchemaProvider();
            var logger = new RecordingLogger<SchemaValidatorFunction>();
            var validator = new SchemaValidatorFunction(logger, schemaProvider);

            var result = await validator.ValidateSchemasAsync(CreateRequest(
                "{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"agents\"]}"));

            Assert.IsFalse(result);
            var rejection = logger.Entries.Single(
                entry => entry.EventId.Id == 150101);
            StringAssert.Contains(
                rejection.Message,
                "'identityTypes' cannot be validated because the GroupMembership schema is not loaded.");
            Assert.AreEqual(
                0,
                logger.Entries.Count(entry =>
                    entry.EventId.Id == 150100 ||
                    entry.EventId.Id == 150102));
        }

        private Task<bool> ValidateAsync(string queryPart) =>
            _schemaValidator.ValidateSchemasAsync(CreateRequest(queryPart));

        private static SchemaValidatorRequest CreateRequest(string queryPart) =>
            new()
            {
                Query = queryPart,
                SyncJob = new SyncJob(),
                CurrentPart = 1,
                TotalParts = 1
            };
    }
}
