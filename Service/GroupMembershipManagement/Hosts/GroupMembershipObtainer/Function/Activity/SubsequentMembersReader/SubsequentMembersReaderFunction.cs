// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class SubsequentMembersReaderFunction
    {
        private readonly ILogger<SubsequentMembersReaderFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly SGMembershipCalculator _calculator;

        public SubsequentMembersReaderFunction(ILogger<SubsequentMembersReaderFunction> logger, IBlobStorageRepository blobStorageRepository, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(SubsequentMembersReaderFunction))]
        public async Task<string> GetMembersAsync([ActivityTrigger] SubsequentMembersReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(SubsequentMembersReaderFunction));
                var response = await _calculator.GetNextTransitiveMembersPageAsync(request.GroupId, request.NextPageUrl);

                if (request.GroupId != request.TargetGroupId)
                {
                    for (int i = 0; i < response.Users.Count; i++)
                    {
                        response.Users[i].SourceGroup = request.GroupId;
                    }
                }

                var serializerSettings = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                };

                var runId = request.SyncJob.RunId.GetValueOrDefault();
                var fileName = $"/{request.TargetGroupId}/userUploads/{runId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
                await _blobStorageRepository.UploadFileAsync(fileName, JsonSerializer.Serialize(response.Users, serializerSettings));
                _logger.FunctionCompleted(nameof(SubsequentMembersReaderFunction));
                return response.NextPageUrl;
            }
        }
    }
}