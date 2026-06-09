// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core.Serialization;
using Hosts.AzureUserReader;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetryContext = Microsoft.Azure.Functions.Worker.RetryContext;

namespace Services.Tests
{

    [TestClass]
    public class StarterFunctionTests
    {
        private string _instanceId;
        private Mock<DurableTaskClient> _durableClientMock;
        private Mock<FunctionContext> _functionContextMock;

        [TestInitialize]
        public void SetupTest()
        {
            _instanceId = "1234567890";
            _durableClientMock = new Mock<DurableTaskClient>("test");
            _functionContextMock = new Mock<FunctionContext>();
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("abc")]
        [DataRow("[{ 'a': 1 }]")]
        [DataRow("{ 'BlobPath':'folder1/folder2/myfile.csv' }")]
        public async Task PostInvalidRequest(string content)
        {

            _durableClientMock
                    .Setup(x => x.ScheduleNewOrchestrationInstanceAsync(It.IsAny<TaskName>(), 
                                                                        It.IsAny<AzureUserReaderRequest>(),
                                                                        It.IsAny<StartOrchestrationOptions>(),
                                                                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(_instanceId);


            var functionContext = new FakeFunctionContext();
            var request = new FakeHttpRequestData(functionContext,
                                                  new Uri("http://localhost/api/StarterFunction"),
                                                  new MemoryStream(Encoding.UTF8.GetBytes(content)));

            var starterFunction = new StarterFunction(NullLogger<StarterFunction>.Instance);
            var result = await starterFunction.HttpStart(
                request,
                _durableClientMock.Object
               );

            Assert.AreEqual(HttpStatusCode.BadRequest, result.StatusCode);
        }

        [TestMethod]
        public async Task PostValidRequest()
        {
            var functionContext = new FakeFunctionContext();
            var durableClient = new FakeDurableTaskClient("test")
            {
                InstanceId = _instanceId
            };

            var request = new FakeHttpRequestData(functionContext,
                                                  new Uri("http://localhost/api/StarterFunction"),
                                                  new MemoryStream(Encoding.UTF8.GetBytes("{ \"ContainerName\":\"myContainer\",\"BlobPath\":\"folder1/folder2/myfile.csv\"}")));

            var starterFunction = new StarterFunction(NullLogger<StarterFunction>.Instance);
            var result = await starterFunction.HttpStart(
                request,
                durableClient
               );

            Assert.AreEqual(HttpStatusCode.Accepted, result.StatusCode);
        }
    }

    public class FakeDurableTaskClient : DurableTaskClient
    {
        public string InstanceId { get; set; }

        public FakeDurableTaskClient(string name) : base(name)
        {
        }

        public override ValueTask DisposeAsync()
        {
            throw new NotImplementedException();
        }

        public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery filter = null)
        {
            throw new NotImplementedException();
        }

        public override Task<OrchestrationMetadata> GetInstancesAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task RaiseEventAsync(string instanceId, string eventName, object eventPayload = null, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task ResumeInstanceAsync(string instanceId, string reason = null, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task<string> ScheduleNewOrchestrationInstanceAsync(TaskName orchestratorName, object input = null, StartOrchestrationOptions options = null, CancellationToken cancellation = default)
        {
            return Task.FromResult(InstanceId);
        }

        public override Task SuspendInstanceAsync(string instanceId, string reason = null, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }

        public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }
    }

    public class FakeHttpRequestData : HttpRequestData
    {
        private readonly Stream _body;

        public FakeHttpRequestData(FunctionContext functionContext, Uri url, Stream body = null) : base(functionContext)
        {
            Url = url;
            _body = body ?? new MemoryStream();
        }

        public override Stream Body => _body;
        public override HttpHeadersCollection Headers { get; } = new HttpHeadersCollection();
        public override IReadOnlyCollection<IHttpCookie> Cookies { get; }
        public override Uri Url { get; }
        public override IEnumerable<ClaimsIdentity> Identities { get; }
        public override string Method { get; }
        public override HttpResponseData CreateResponse()
        {
            return new FakeHttpResponseData(FunctionContext);
        }
    }

    public class FakeHttpResponseData : HttpResponseData
    {
        public FakeHttpResponseData(FunctionContext functionContext) : base(functionContext)
        {
        }

        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new HttpHeadersCollection();
        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpCookies Cookies { get; }
    }

    public class FakeFunctionContext : FunctionContext
    {
        private readonly IServiceProvider _serviceProvider;

        public FakeFunctionContext(IServiceProvider serviceProvider = null)
        {
            _serviceProvider = serviceProvider ?? CreateDefaultServiceProvider();
            InstanceServices = _serviceProvider;
        }

        public override string InvocationId { get; }
        public override string FunctionId { get; }
        public override TraceContext TraceContext { get; }
        public override BindingContext BindingContext { get; }
        public override RetryContext RetryContext { get; }
        public override IServiceProvider InstanceServices { get; set; }
        public override FunctionDefinition FunctionDefinition { get; }
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features { get; }

        private static IServiceProvider CreateDefaultServiceProvider()
        {
            var services = new ServiceCollection();
            
            // Add the JSON serializer options for Azure Functions Worker
            services.AddSingleton<System.Text.Json.JsonSerializerOptions>(new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            // Add the WorkerOptions with ObjectSerializer required by CreateCheckStatusResponseAsync
            var workerOptions = new WorkerOptions
            {
                Serializer = new JsonObjectSerializer()
            };
            services.AddSingleton<IOptions<WorkerOptions>>(Options.Create(workerOptions));
            
            return services.BuildServiceProvider();
        }
    }

}