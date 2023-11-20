// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hosts.DestinationAttributesUpdater;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {

        private Mock<IDestinationAttributesUpdaterService> _mockDestinationAttributeUpdaterService;
        private Mock<ILoggingRepository> _mockLoggingRepository;
        Mock<IDurableOrchestrationContext> _context;


        private const string GroupMembershipDestinationType = "GroupMembership";
        private const string TeamsChannelMemberhsipDestinationType = "TeamsChannelMembership";

        private List<DestinationAttributes> _attributeReaderResponse;
        List<(AzureADGroup Destination, Guid JobId)> _destinationReaderResponse;


        private const string EmailSubject = "EmailSubject";
        private const string SyncStartedEmailBody = "SyncStartedEmailBody";
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";

        [TestInitialize]
        public void InitializeTest()
        {
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _mockDestinationAttributeUpdaterService = new Mock<IDestinationAttributesUpdaterService>();
            _context = new Mock<IDurableOrchestrationContext>();

            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(AttributeCacheUpdaterFunction)), It.IsAny<DestinationAttributes>()))
                    .Callback<string, object>(async (name, request) =>
                    {
                        await CallAttributeCacheUpdaterAsync();
                    });

            _context.Setup(x => x.CallActivityAsync<List<DestinationAttributes>>(It.Is<string>(x => x == nameof(AttributeReaderFunction)), It.IsAny<AttributeReaderRequest>()))
                     .Callback<string, object>(async (name, request) =>
                     {
                         _attributeReaderResponse = await CallAttributeReaderAsync(request as AttributeReaderRequest);
                     })
                     .ReturnsAsync(() => _attributeReaderResponse);

            _context.Setup(x => x.CallActivityAsync<List<(AzureADGroup Destination, Guid JobId)>>(It.Is<string>(x => x == nameof(DestinationReaderFunction)), It.Is<string>(x => x == GroupMembershipDestinationType)))
                    .Callback<string, object>(async (name, request) =>
                    {
                        _destinationReaderResponse = await CallDestinationReaderAsync(request as string);
                    })
                    .ReturnsAsync(() => _destinationReaderResponse);

            _context.Setup(x => x.CallActivityAsync<List<(AzureADGroup Destination, Guid JobId)>>(It.Is<string>(x => x == nameof(DestinationReaderFunction)), It.Is<string>(x => x == TeamsChannelMemberhsipDestinationType)))
                    .Callback<string, object>(async (name, request) =>
                    {
                        _destinationReaderResponse = new List<(AzureADGroup Destination, Guid JobId)>();
                    })
                    .ReturnsAsync(() => _destinationReaderResponse);

            _context.Setup(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(LoggerFunction)), It.IsAny<LoggerRequest>()))
                   .Callback<string, object>(async (name, request) =>
                   {
                       await CallLoggerFunctionAsync(request as LoggerRequest);
                   });
        }

        public Guid getDestinationObjectId(SyncJob job)
        {
            return new Guid((JArray.Parse(job.Destination)[0] as JObject)["value"]["objectId"].Value<string>());
        }

        [TestMethod]
        public async Task TestSuccessfulRun()
        {
            var destination1 = new AzureADGroup { ObjectId = Guid.NewGuid() };
            var jobId1 = Guid.NewGuid();
            var destinations = new List<(AzureADGroup Destination, Guid JobId)>() { (destination1, jobId1) };
            _mockDestinationAttributeUpdaterService.Setup(x => x.GetDestinationsAsync(It.IsAny<string>())).ReturnsAsync(() => destinations);

            var destinationAttributes1 = new DestinationAttributes
            {
                Id = jobId1,
                Name = "Name",
                Owners = new List<Guid>() { Guid.NewGuid() }
            };
            _mockDestinationAttributeUpdaterService.Setup(x => x.GetBulkDestinationAttributesAsync(It.IsAny<List<(AzureADGroup Destination, Guid JobId)>>(), It.IsAny<string>())).ReturnsAsync(() => new List<DestinationAttributes>() { destinationAttributes1 });

            var orchestrator = new OrchestratorFunction();
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync<List<(AzureADGroup Destination, Guid JobId)>>(It.Is<string>(x => x == nameof(DestinationReaderFunction)), It.IsAny<string>()),
                                Times.Exactly(2));
            _context.Verify(x => x.CallActivityAsync<List<DestinationAttributes>>(It.Is<string>(x => x == nameof(AttributeReaderFunction)), It.Is<AttributeReaderRequest>(x => x.Destinations[0].Destination.ObjectId == destinations[0].Destination.ObjectId)),
                                Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<string>(x => x == nameof(AttributeCacheUpdaterFunction)), It.Is<DestinationAttributes>(x => x == destinationAttributes1)),
                                Times.Once());
        }

        private async Task CallAttributeCacheUpdaterAsync()
        {
            var AttributeCacheUpdaterFunction = new AttributeCacheUpdaterFunction(_mockLoggingRepository.Object, _mockDestinationAttributeUpdaterService.Object);
            await AttributeCacheUpdaterFunction.UpdateAttributesAsync(new DestinationAttributes());
        }

        private async Task<List<DestinationAttributes>> CallAttributeReaderAsync(AttributeReaderRequest request)
        {
            var AttributeReaderFunction = new AttributeReaderFunction(_mockLoggingRepository.Object, _mockDestinationAttributeUpdaterService.Object);
            var AttributeReaderRequest = new AttributeReaderRequest
            {
                Destinations = new List<(AzureADGroup Destination, Guid JobId)>(),
                DestinationType = GroupMembershipDestinationType
            };
            var response = await AttributeReaderFunction.GetAttributesAsync(request);
            return response;
        }

        private async Task<List<(AzureADGroup Destination, Guid JobId)>> CallDestinationReaderAsync(string destinationType)
        {
            var DestinationReaderFunction = new DestinationReaderFunction(_mockLoggingRepository.Object, _mockDestinationAttributeUpdaterService.Object);
            var response = await DestinationReaderFunction.GetDestinationsAsync(destinationType);
            return response;
        }

        private async Task CallLoggerFunctionAsync(LoggerRequest request)
        {
            var loggerFunction = new LoggerFunction(_mockLoggingRepository.Object);
            await loggerFunction.LogMessageAsync(request);
        }
    }
}
