// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Hosts.AzureUserReader;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Models.Entities;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class UserCreatorSubOrchestratorTests
    {
        [TestMethod]
        public async Task CreateUsersAsync()
        {
            var loggingRepository = new Mock<ILoggingRepository>();
            var graphUserRepository = new Mock<IGraphUserRepository>();
            var context = new Mock<IDurableOrchestrationContext>();

            var personnelNumbers = new List<string>();
            for (int i = 1; i <= 10000; i++)
            {
                personnelNumbers.Add(i.ToString());
            }

            var request = new AzureUserCreatorRequest
            {
                PersonnelNumbers = personnelNumbers,
                TenantInformation = new TenantInformation
                {
                    CountryCode = "US",
                    EmailPrefix = "testusers",
                    TenantDomain = "M365x000000.OnMicrosoft.com"
                }
            };

            loggingRepository.Setup(x => x.LogMessageAsync(It.IsAny<LogMessage>(), VerbosityLevel.DEBUG, It.IsAny<string>(), It.IsAny<string>()));
            context.Setup(x => x.GetInput<AzureUserCreatorRequest>()).Returns(request);

            var currentProfilePage = default(List<GraphProfileInformation>);
            context.Setup(x => x.CallActivityAsync<List<GraphProfileInformation>>(It.IsAny<string>(), It.IsAny<AzureUserCreatorRequest>()))
                 .Callback<string, object>(async (name, request) =>
                 {
                     currentProfilePage = await RunAzureUserCreatorFunctionAsync
                                                (
                                                    graphUserRepository.Object,
                                                    loggingRepository.Object,
                                                    request as AzureUserCreatorRequest
                                                );
                 })
                 .ReturnsAsync(() => currentProfilePage);

            var addedUsers = default(List<GraphProfileInformation>);
            graphUserRepository.Setup(x => x.AddUsersAsync(It.IsAny<List<User>>(), It.IsAny<Guid?>()))
                .Callback<List<User>, Guid?>((users, runId) =>
               {
                   addedUsers = new List<GraphProfileInformation>();
                   foreach (var user in users)
                   {
                       addedUsers.Add(new GraphProfileInformation
                       {
                           Id = Guid.NewGuid().ToString(),
                           PersonnelNumber = user.OnPremisesImmutableId,
                           UserPrincipalName = user.UserPrincipalName
                       });
                   }
               })
                .ReturnsAsync(() => addedUsers);

            var function = new UserCreatorSubOrchestratorFunction(loggingRepository.Object);
            var newUsers = await function.CreateUsersAsync(context.Object);

            Assert.AreEqual(personnelNumbers.Count, newUsers.Count);
            Assert.IsTrue(newUsers.All(x => !string.IsNullOrWhiteSpace(x.PersonnelNumber)));
            Assert.IsTrue(newUsers.All(x => !string.IsNullOrWhiteSpace(x.Id)));
            Assert.IsTrue(newUsers.All(x => !string.IsNullOrWhiteSpace(x.UserPrincipalName)));
        }

        private async Task<List<GraphProfileInformation>> RunAzureUserCreatorFunctionAsync(IGraphUserRepository graphUserRepository, ILoggingRepository loggingRepository, AzureUserCreatorRequest request)
        {
            var function = new AzureUserCreatorFunction(graphUserRepository, loggingRepository);
            return await function.AddUsersAsync(request);
        }
    }
}
