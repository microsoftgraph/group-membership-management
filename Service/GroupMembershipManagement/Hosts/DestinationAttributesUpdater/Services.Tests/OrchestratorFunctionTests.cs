// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.DestinationAttributesUpdater;
using Microsoft.DurableTask;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Moq;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Services.Tests
{
    [TestClass]
    public class OrchestratorFunctionTests
    {

        private Mock<IDestinationAttributesUpdaterService> _mockDestinationAttributeUpdaterService;
        private Mock<ILoggingRepository> _mockLoggingRepository;
        Mock<TaskOrchestrationContext> _context;


        private const string GroupMembershipDestinationType = "GroupMembership";
        private const string TeamsChannelMemberhsipDestinationType = "TeamsChannelMembership";

        private List<DestinationAttributes> _attributeReaderResponse;
        List<DestinationReaderResponse> _destinationReaderResponse;


        private const string EmailSubject = "EmailSubject";
        private const string SyncStartedEmailBody = "SyncStartedEmailBody";
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";

        private JsonSerializerOptions _destinationObjectSerializerOptions;

        [TestInitialize]
        public void InitializeTest()
        {
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _mockDestinationAttributeUpdaterService = new Mock<IDestinationAttributesUpdaterService>();
            _context = new Mock<TaskOrchestrationContext>();

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.ToString()  == nameof(AttributeCacheUpdaterFunction)), It.IsAny<DestinationAttributes>(),It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallAttributeCacheUpdaterAsync();
                    });

            _context.Setup(x => x.CallActivityAsync<List<DestinationAttributes>>(It.Is<TaskName>(x => x.ToString()  == nameof(AttributeReaderFunction)), It.IsAny<AttributeReaderRequest>(), It.IsAny<TaskOptions>()))
                     .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                     {
                         _attributeReaderResponse = await CallAttributeReaderAsync(request as AttributeReaderRequest);
                     })
                     .ReturnsAsync(() => _attributeReaderResponse);

            _context.Setup(x => x.CallActivityAsync<List<DestinationReaderResponse>>(It.Is<TaskName>(x => x.ToString()  == nameof(DestinationReaderFunction)), It.Is<string>(x => x  == GroupMembershipDestinationType), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        _destinationReaderResponse = await CallDestinationReaderAsync(request as string);
                    })
                    .ReturnsAsync(() => _destinationReaderResponse);

            _context.Setup(x => x.CallActivityAsync<List<DestinationReaderResponse>>(It.Is<TaskName>(x => x.ToString()  == nameof(DestinationReaderFunction)), It.Is<string>(x => x == TeamsChannelMemberhsipDestinationType), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        _destinationReaderResponse = new List<DestinationReaderResponse>();
                    })
                    .ReturnsAsync(() => _destinationReaderResponse);

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.ToString()  == nameof(LoggerFunction)), It.IsAny<LoggerRequest>(),null))
                   .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                   {
                       await CallLoggerFunctionAsync(request as LoggerRequest);
                   });

            _destinationObjectSerializerOptions = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
        }

        public Guid getDestinationObjectId(SyncJob job)
        {
            return new Guid((JArray.Parse(job.Destination)[0] as JObject)["value"]["objectId"].Value<string>());
        }

        [TestMethod]
        public async Task TestSuccessfulRun()
        {
            var destination1 = new DestinationObject() { Type = "GroupMembership", Value = new GroupDestinationValue() { ObjectId = Guid.NewGuid() } };
            var serializedDestination1 = SerializeDestination(destination1);
            var jobId1 = Guid.NewGuid();
            var destinations = new List<DestinationReaderResponse>() { new DestinationReaderResponse { Destination = serializedDestination1, JobId = jobId1 } };
            _mockDestinationAttributeUpdaterService.Setup(x => x.GetDestinationsAsync(It.IsAny<string>())).ReturnsAsync(() => destinations);

            var destinationAttributes1 = new DestinationAttributes
            {
                Id = jobId1,
                Name = "Name",
                Owners = new List<Guid>() { Guid.NewGuid() }
            };
            _mockDestinationAttributeUpdaterService.Setup(x => x.GetBulkDestinationAttributesAsync(It.IsAny<List<DestinationReaderResponse>>(), It.IsAny<string>())).ReturnsAsync(() => new List<DestinationAttributes>() { destinationAttributes1 });

            var orchestrator = new OrchestratorFunction();
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync<List<DestinationReaderResponse>>(It.Is<TaskName>(x => x.ToString()  == nameof(DestinationReaderFunction)), It.IsAny<string>(),null),
                                Times.Exactly(2));
            _context.Verify(x => x.CallActivityAsync<List<DestinationAttributes>>(It.Is<TaskName>(x => x.ToString()  == nameof(AttributeReaderFunction)), It.Is<AttributeReaderRequest>(x => DeserializeDestination(x.Destinations[0].Destination).Value.ObjectId == DeserializeDestination(destinations[0].Destination).Value.ObjectId),null),
                                Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x.ToString()  == nameof(AttributeCacheUpdaterFunction)), It.Is<DestinationAttributes>(x => x == destinationAttributes1),null),
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
            var response = await AttributeReaderFunction.GetAttributesAsync(request);
            return response;
        }

        private async Task<List<DestinationReaderResponse>> CallDestinationReaderAsync(string destinationType)
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

        private string SerializeDestination(DestinationObject destination)
        {
            return JsonSerializer.Serialize(destination, _destinationObjectSerializerOptions);
        }

        private DestinationObject DeserializeDestination(string destination)
        {
            return JsonSerializer.Deserialize<DestinationObject>(destination, _destinationObjectSerializerOptions);
        }
    }
}
