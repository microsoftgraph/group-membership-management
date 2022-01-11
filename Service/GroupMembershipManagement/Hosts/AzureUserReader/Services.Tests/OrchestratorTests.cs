// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Hosts.AzureUserReader;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorTests
    {
        [TestMethod]
        public async Task RunOrchestratorValidTestAsync()
        {
            var loggingRepository = new Mock<ILoggingRepository>();
            var context = new Mock<IDurableOrchestrationContext>();
            var request = new AzureUserReaderRequest
            {
                BlobPath = "blob/path/blob.csv",
                ContainerName = "myContainer"
            };

            var totalPersonnelNumbers = 10001;
            var personnelNumbers = new List<string>();
            for (var i = 0; i < totalPersonnelNumbers; i++)
            {
                personnelNumbers.Add(i.ToString());
            }

            var allProfiles = new List<GraphProfileInformation>();
            var currentPage = new List<GraphProfileInformation>();

            loggingRepository.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), It.IsAny<string>(), It.IsAny<string>()));
            context.Setup(x => x.GetInput<AzureUserReaderRequest>()).Returns(request);
            context.Setup(x => x.CallActivityAsync<IList<string>>(It.IsAny<string>(), It.IsAny<AzureUserReaderRequest>())).ReturnsAsync(personnelNumbers);
            context.Setup(x => x.CallActivityAsync<IList<GraphProfileInformation>>(It.IsAny<string>(), It.IsAny<List<string>>()))
                .Callback<string, object>((name, request) =>
                {
                    var profiles = new List<GraphProfileInformation>();
                    var personnelNumbers = request as List<string>;
                    foreach (var personnelNumber in personnelNumbers)
                    {
                        profiles.Add(new GraphProfileInformation
                        {
                            Id = Guid.NewGuid().ToString(),
                            PersonnelNumber = personnelNumber
                        });
                    }

                    currentPage = profiles;
                    allProfiles.AddRange(profiles);

                }).ReturnsAsync(() => currentPage);

            var orchestrator = new OrchestratorFunction(loggingRepository.Object);
            await orchestrator.RunOrchestrator(context.Object);

            Assert.AreEqual(personnelNumbers.Count, allProfiles.Count);

            var pages = totalPersonnelNumbers / 1000M;
            var fullPages = (int)Math.Truncate(pages);
            var totalPages = (pages - fullPages) == 0 ? fullPages : fullPages + 1;

            context.Verify(x => x.CallActivityAsync<IList<GraphProfileInformation>>(It.IsAny<string>(), It.IsAny<List<string>>()), Times.Exactly(totalPages));
            context.Verify(x => x.CallActivityAsync(It.IsAny<string>(), It.IsAny<UploadUsersRequest>()), Times.Once());
            loggingRepository.Verify(x => x.LogMessageAsync(
                                           It.Is<LogMessage>(m => m.Message.StartsWith($"{nameof(OrchestratorFunction)} function completed")),
                                           It.IsAny<string>(),
                                           It.IsAny<string>()), Times.Once());
        }
    }
}
