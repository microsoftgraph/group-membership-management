// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class NonProdServiceFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly INonProdService _nonProdService = null;
        private readonly IGraphGroupRepository _graphGroupRepository = null;

        public NonProdServiceFunction(ILoggingRepository loggingRepository, INonProdService nonProdService, IGraphGroupRepository graphGroupRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _nonProdService = nonProdService ?? throw new ArgumentNullException(nameof(nonProdService));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        [FunctionName(nameof(NonProdServiceFunction))]
        public async Task<HttpResponseMessage> HttpStart([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(NonProdServiceFunction)} function started" });

            await _nonProdService.CreateTestGroups();
            await _nonProdService.FillTestGroups();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(NonProdServiceFunction)} function completed" });

            var response = new HttpResponseMessage { StatusCode = HttpStatusCode.OK };
            return response;
        }
    }
}
