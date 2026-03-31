// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.DestinationAttributesUpdater;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Moq;
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
        Mock<TaskOrchestrationContext> _context;

        private const string GroupMembershipDestinationType = "GroupMembership";
        private const string TeamsChannelMemberhsipDestinationType = "TeamsChannelMembership";

        private List<DestinationAttributes> _attributeReaderResponse;
        List<DestinationInfo> _destinationReaderResponse;

        private JsonSerializerOptions _destinationObjectSerializerOptions;

        [TestInitialize]
        public void InitializeTest()
        {
            _mockDestinationAttributeUpdaterService = new Mock<IDestinationAttributesUpdaterService>();
            _context = new Mock<TaskOrchestrationContext>();

            _context.Setup(x => x.CreateReplaySafeLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

            _context.Setup(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(AttributeCacheUpdaterFunction)), It.IsAny<DestinationAttributes>(), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        await CallAttributeCacheUpdaterAsync();
                    });

            _context.Setup(x => x.CallActivityAsync<List<DestinationAttributes>>(It.Is<TaskName>(x => x.Name == nameof(AttributeReaderFunction)), It.IsAny<AttributeReaderRequest>(), It.IsAny<TaskOptions>()))
                     .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                     {
                         _attributeReaderResponse = await CallAttributeReaderAsync(request as AttributeReaderRequest);
                     })
                     .ReturnsAsync(() => _attributeReaderResponse);

            _context.Setup(x => x.CallActivityAsync<List<DestinationInfo>>(It.Is<TaskName>(x => x.Name == nameof(DestinationReaderFunction)), It.Is<string>(x => x == GroupMembershipDestinationType), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        _destinationReaderResponse = await CallDestinationReaderAsync(request as string);
                    })
                    .ReturnsAsync(() => _destinationReaderResponse);

            _context.Setup(x => x.CallActivityAsync<List<DestinationInfo>>(It.Is<TaskName>(x => x.Name == nameof(DestinationReaderFunction)), It.Is<string>(x => x == TeamsChannelMemberhsipDestinationType), It.IsAny<TaskOptions>()))
                    .Callback<TaskName, object, TaskOptions>(async (name, request, options) =>
                    {
                        _destinationReaderResponse = new List<DestinationInfo>();
                    })
                    .ReturnsAsync(() => _destinationReaderResponse);

            _destinationObjectSerializerOptions = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
        }

        public Guid getDestinationObjectId(SyncJob job)
        {
            return job.Group.GroupId;
        }

        [TestMethod]
        public async Task TestSuccessfulRun()
        {
            var destination1 = new DestinationObject() { Type = "GroupMembership", Value = new GroupDestinationValue() { ObjectId = Guid.NewGuid() } };
            var serializedDestination1 = SerializeDestination(destination1);
            var jobId1 = Guid.NewGuid();
            var destinations = new List<DestinationInfo>() { new DestinationInfo { Destination = serializedDestination1, JobId = jobId1 } };
            _mockDestinationAttributeUpdaterService.Setup(x => x.GetDestinationsAsync(It.IsAny<string>())).ReturnsAsync(() => destinations);

            var destinationAttributes1 = new DestinationAttributes
            {
                Id = jobId1,
                Name = "Name",
                Owners = new List<Guid>() { Guid.NewGuid() }
            };
            _mockDestinationAttributeUpdaterService.Setup(x => x.GetBulkDestinationAttributesAsync(It.IsAny<List<DestinationInfo>>(), It.IsAny<string>())).ReturnsAsync(() => new List<DestinationAttributes>() { destinationAttributes1 });

            var orchestrator = new OrchestratorFunction();
            await orchestrator.RunOrchestratorAsync(_context.Object);

            _context.Verify(x => x.CallActivityAsync<List<DestinationInfo>>(It.Is<TaskName>(x => x.Name == nameof(DestinationReaderFunction)), It.IsAny<string>(), It.IsAny<TaskOptions>()),
                                Times.Exactly(2));
            _context.Verify(x => x.CallActivityAsync<List<DestinationAttributes>>(It.Is<TaskName>(x => x.Name == nameof(AttributeReaderFunction)), It.Is<AttributeReaderRequest>(x => DeserializeDestination(x.Destinations[0].Destination).Value.ObjectId == DeserializeDestination(destinations[0].Destination).Value.ObjectId), It.IsAny<TaskOptions>()),
                                Times.Once());
            _context.Verify(x => x.CallActivityAsync(It.Is<TaskName>(x => x.Name == nameof(AttributeCacheUpdaterFunction)), It.Is<DestinationAttributes>(x => x == destinationAttributes1), It.IsAny<TaskOptions>()),
                                Times.Once());
        }

        private async Task CallAttributeCacheUpdaterAsync()
        {
            var attributeCacheUpdaterFunction = new AttributeCacheUpdaterFunction(NullLogger<AttributeCacheUpdaterFunction>.Instance, _mockDestinationAttributeUpdaterService.Object);
            await attributeCacheUpdaterFunction.UpdateAttributesAsync(new DestinationAttributes());
        }

        private async Task<List<DestinationAttributes>> CallAttributeReaderAsync(AttributeReaderRequest request)
        {
            var attributeReaderFunction = new AttributeReaderFunction(NullLogger<AttributeReaderFunction>.Instance, _mockDestinationAttributeUpdaterService.Object);
            var response = await attributeReaderFunction.GetAttributesAsync(request);
            return response;
        }

        private async Task<List<DestinationInfo>> CallDestinationReaderAsync(string destinationType)
        {
            var destinationReaderFunction = new DestinationReaderFunction(NullLogger<DestinationReaderFunction>.Instance, _mockDestinationAttributeUpdaterService.Object);
            var response = await destinationReaderFunction.GetDestinationsAsync(destinationType);
            return response;
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
