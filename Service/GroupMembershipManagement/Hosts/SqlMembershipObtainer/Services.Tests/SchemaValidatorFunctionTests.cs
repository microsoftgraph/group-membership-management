// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Services.Tests.Helpers;
using SqlMembershipObtainer;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class SchemaValidatorFunctionTests
    {
        private SchemaValidatorFunction _schemaValidatorFunction;
        private SyncJob _syncJob;

        [TestInitialize]
        public void Setup()
        {
            _schemaValidatorFunction = new SchemaValidatorFunction(NullLogger<SchemaValidatorFunction>.Instance, SchemaProviderFactory.CreateJsonSchemaProvider());
            _syncJob = SqlMembershipJobCreator.CreateSampleSyncJobs(1, "SqlMembership", 24).First();
        }

        [TestMethod]
        [DataRow("{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"}}")]
        [DataRow("{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"users\"]}")]
        [DataRow("{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"agents\"]}")]
        [DataRow("{\"type\":\"SqlMembership\",\"source\":{\"manager\":{\"id\":1}},\"identityTypes\":[\"users\",\"agents\"]}")]
        public async Task AcceptsQueryPartWithValidIdentityTypesAsync(string queryPart)
        {
            Assert.IsTrue(await ValidateAsync(queryPart));
        }

        [TestMethod]
        [DataRow("{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[]}")]
        [DataRow("{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"devices\"]}")]
        [DataRow("{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":[\"users\",\"users\"]}")]
        [DataRow("{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"Department = 'Sales'\"},\"identityTypes\":\"users\"}")]
        public async Task RejectsQueryPartWithInvalidIdentityTypesAsync(string queryPart)
        {
            Assert.IsFalse(await ValidateAsync(queryPart));
        }

        private Task<bool> ValidateAsync(string queryPart)
        {
            return _schemaValidatorFunction.ValidateSchemasAsync(new SchemaValidatorRequest
            {
                Query = queryPart,
                SyncJob = _syncJob,
                CurrentPart = 1,
                TotalParts = 1
            });
        }
    }
}
