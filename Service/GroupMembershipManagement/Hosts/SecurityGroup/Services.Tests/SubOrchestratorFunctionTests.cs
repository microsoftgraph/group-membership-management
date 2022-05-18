// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Hosts.SecurityGroup;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Tests.Services
{
    [TestClass]
    public class SubOrchestratorFunctionTests
    {
        private const int Number_Of_Pages = 2;

        private Mock<IDryRunValue> _dryRunValue;
        private Mock<IMailRepository> _mailRepository;
        private Mock<ILoggingRepository> _loggingRepository;
        private Mock<ISyncJobRepository> _syncJobRepository;
        private Mock<IGraphGroupRepository> _graphGroupRepository;
        private Mock<IEmailSenderRecipient> _emailSenderRecipient;
        private Mock<IBlobStorageRepository> _blobStorageRepository;
        private Mock<IDurableOrchestrationContext> _durableOrchestrationContext;

        private int _userCount;
        private bool _groupExists;
        private string _usersReaderNextPageUrl;
        private SecurityGroupRequest _securityGroupRequest;
        private SGMembershipCalculator _membershipCalculator;
        private (List<AzureADUser> users,
                 Dictionary<string, int> nonUserGraphObjects,
                 string nextPageUrl,
                 IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup) _usersReaderResponse;

        [TestInitialize]
        public void Setup()
        {
            _dryRunValue = new Mock<IDryRunValue>();
            _mailRepository = new Mock<IMailRepository>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<ISyncJobRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _emailSenderRecipient = new Mock<IEmailSenderRecipient>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _durableOrchestrationContext = new Mock<IDurableOrchestrationContext>();

            _userCount = 10;
            _groupExists = true;

            var syncJob = new SyncJob
            {
                RowKey = Guid.NewGuid().ToString(),
                PartitionKey = "00-00-0000",
                TargetOfficeGroupId = Guid.NewGuid(),
                Query = QuerySample.GenerateQuerySample("SecurityGroup").GetQuery(),
                Status = "InProgress",
                Period = 6
            };

            _securityGroupRequest = new SecurityGroupRequest
            {
                RunId = Guid.NewGuid(),
                SourceGroup = new Entities.AzureADGroup { ObjectId = Guid.NewGuid() },
                SyncJob = syncJob
            };

            _membershipCalculator = new SGMembershipCalculator(
                                            _graphGroupRepository.Object,
                                            _blobStorageRepository.Object,
                                            _mailRepository.Object,
                                            _emailSenderRecipient.Object,
                                            _syncJobRepository.Object,
                                            _loggingRepository.Object,
                                            _dryRunValue.Object
                                            );

            _durableOrchestrationContext.Setup(x => x.GetInput<SecurityGroupRequest>()).Returns(() => _securityGroupRequest);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<bool>(It.IsAny<string>(), It.IsAny<GroupValidatorRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _groupExists = await CallGroupValidatorFunctionAsync(request as GroupValidatorRequest);
                                        })
                                        .ReturnsAsync(() => _groupExists);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<(List<AzureADUser> users,
                                                                Dictionary<string, int> nonUserGraphObjects,
                                                                string nextPageUrl,
                                                                IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)>(It.IsAny<string>(),
                                                                                                                                     It.IsAny<UsersReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _usersReaderResponse = await CallUsersReaderFunctionAsync(request as UsersReaderRequest);
                                        })
                                        .ReturnsAsync(() => _usersReaderResponse);

            _durableOrchestrationContext.Setup(x => x.CallActivityAsync<(List<AzureADUser> users,
                                                    Dictionary<string, int> nonUserGraphObjects,
                                                    string nextPageUrl,
                                                    IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)>(It.IsAny<string>(),
                                                                                                                         It.IsAny<SubsequentUsersReaderRequest>()))
                                        .Callback<string, object>(async (name, request) =>
                                        {
                                            _usersReaderResponse = await CallSubsequentUsersReaderFunctionAsync(request as SubsequentUsersReaderRequest);
                                        })
                                        .ReturnsAsync(() => _usersReaderResponse);

            _graphGroupRepository.Setup(x => x.GroupExists(It.IsAny<Guid>())).ReturnsAsync(() => _groupExists);
            _graphGroupRepository.Setup(x => x.GetFirstUsersPageAsync(It.IsAny<Guid>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();
                                     var nonUserGraphObjects = new Dictionary<string, int> { { "non-user-object", 1 } };
                                     var usersPage = new Mock<IGroupTransitiveMembersCollectionWithReferencesPage>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, nonUserGraphObjects, _usersReaderNextPageUrl, usersPage.Object);
                                 });

            _graphGroupRepository.Setup(x => x.GetNextUsersPageAsync(It.IsAny<string>(), It.IsAny<IGroupTransitiveMembersCollectionWithReferencesPage>()))
                                 .ReturnsAsync(() =>
                                 {
                                     var users = new List<AzureADUser>();
                                     var nonUserGraphObjects = new Dictionary<string, int> { { "non-user-object", 1 } };
                                     var usersPage = new Mock<IGroupTransitiveMembersCollectionWithReferencesPage>();

                                     for (var i = 0; i < _userCount; i++)
                                     {
                                         users.Add(new AzureADUser { ObjectId = Guid.NewGuid() });
                                     }

                                     return (users, nonUserGraphObjects, null, usersPage.Object);
                                 });
        }

        [TestMethod]
        public async Task ProcessSinglePageRequestTestAsync()
        {
            var subOrchestratorFunction = new SubOrchestratorFunction(_loggingRepository.Object);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists.")),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()
                               ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetFirstUsersPageAsync(It.IsAny<Guid>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.Contains($"read {_userCount} users")),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function completed"),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            Assert.IsNotNull(Users);
            Assert.AreEqual(_userCount, Users.Count);
            Assert.AreEqual(SyncStatus.InProgress, Status);
        }

        [TestMethod]
        public async Task ProcessMultiplePageRequestTestAsync()
        {
            _usersReaderNextPageUrl = "http://next-page-url";

            var subOrchestratorFunction = new SubOrchestratorFunction(_loggingRepository.Object);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                   It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists.")),
                                   It.IsAny<string>(),
                                   It.IsAny<string>()
                               ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                    It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                    It.IsAny<string>(),
                                    It.IsAny<string>()
                                ), Times.Once);

            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);
            _graphGroupRepository.Verify(x => x.GetFirstUsersPageAsync(It.IsAny<Guid>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message.Contains($"read {_userCount * Number_Of_Pages} users")),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                        It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function completed"),
                        It.IsAny<string>(),
                        It.IsAny<string>()
                    ), Times.Once);

            Assert.IsNotNull(Users);
            Assert.AreEqual(_userCount * Number_Of_Pages, Users.Count);
            Assert.AreEqual(SyncStatus.InProgress, Status);
        }

        [TestMethod]
        public async Task GroupDoesNotExistTestAsync()
        {
            _groupExists = false;

            var subOrchestratorFunction = new SubOrchestratorFunction(_loggingRepository.Object);
            var (Users, Status) = await subOrchestratorFunction.RunSubOrchestratorAsync(_durableOrchestrationContext.Object);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message == $"{nameof(SubOrchestratorFunction)} function started"),
                                It.IsAny<string>(),
                                It.IsAny<string>()
                            ), Times.Once);

            _graphGroupRepository.Verify(x => x.GroupExists(It.IsAny<Guid>()), Times.Once);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} exists")),
                                It.IsAny<string>(),
                                It.IsAny<string>()
                            ), Times.Never);

            _loggingRepository.Verify(x => x.LogMessageAsync(
                                It.Is<LogMessage>(m => m.Message.Contains($"Group with ID {_securityGroupRequest.SourceGroup.ObjectId} doesn't exist")),
                                It.IsAny<string>(),
                                It.IsAny<string>()
                            ), Times.Once);

            Assert.IsNull(Users);
            Assert.AreEqual(SyncStatus.SecurityGroupNotFound, Status);
        }

        private async Task<bool> CallGroupValidatorFunctionAsync(GroupValidatorRequest request)
        {
            var function = new GroupValidatorFunction(_loggingRepository.Object, _membershipCalculator, _emailSenderRecipient.Object);
            return await function.ValidateGroupAsync(request);
        }

        private async Task<(List<AzureADUser> users,
                            Dictionary<string, int> nonUserGraphObjects,
                            string nextPageUrl,
                            IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> CallUsersReaderFunctionAsync(UsersReaderRequest request)
        {
            var function = new UsersReaderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetUsersAsync(request);
        }

        private async Task<(List<AzureADUser> users,
                    Dictionary<string, int> nonUserGraphObjects,
                    string nextPageUrl,
                    IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> CallSubsequentUsersReaderFunctionAsync(SubsequentUsersReaderRequest request)
        {
            var function = new SubsequentUsersReaderFunction(_loggingRepository.Object, _membershipCalculator);
            return await function.GetUsersAsync(request);
        }
    }
}

