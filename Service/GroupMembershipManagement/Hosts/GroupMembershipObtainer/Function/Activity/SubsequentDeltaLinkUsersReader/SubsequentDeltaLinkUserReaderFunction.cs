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
    public class SubsequentDeltaLinkUserReaderFunction
    {
        private readonly ILogger<SubsequentDeltaLinkUserReaderFunction> _logger;
        private readonly SGMembershipCalculator _calculator;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public SubsequentDeltaLinkUserReaderFunction(ILogger<SubsequentDeltaLinkUserReaderFunction> logger, SGMembershipCalculator calculator, IBlobStorageRepository blobStorageRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(SubsequentDeltaLinkUserReaderFunction))]
        public async Task<DeltaUrls> GetSubsequentDeltaLinkUsersAsync([ActivityTrigger] SubsequentDeltaLinkUserReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(SubsequentDeltaLinkUserReaderFunction));

                var response = await _calculator.GetNextDeltaLinkUsersPageAsync(request.GroupId, request.NextPageUrl, request.PageCount);

                if (request.GroupId != request.TargetGroupId)
                {
                    for (int i = 0; i < response.UsersToAdd.Count; i++)
                    {
                        response.UsersToAdd[i].SourceGroup = request.GroupId;
                    }
                    for (int i = 0; i < response.UsersToRemove.Count; i++)
                    {
                        response.UsersToRemove[i].SourceGroup = request.GroupId;
                    }
                }

                var serializerSettings = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                };

                var runId = request.SyncJob.RunId.GetValueOrDefault();
                var fileName = $"/{request.TargetGroupId}/userUploads/deltaLink/adds/{runId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
                await _blobStorageRepository.UploadFileAsync(fileName, JsonSerializer.Serialize(response.UsersToAdd, serializerSettings));

                var fileNameRemove = $"/{request.TargetGroupId}/userUploads/deltaLink/removes/{runId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
                await _blobStorageRepository.UploadFileAsync(fileNameRemove, JsonSerializer.Serialize(response.UsersToRemove, serializerSettings));

                _logger.FunctionCompleted(nameof(SubsequentDeltaLinkUserReaderFunction));
                return new DeltaUrls
                {
                    NextPageUrl = response.NextPageUrl,
                    DeltaUrl = response.DeltaUrl
                };
            }
        }
    }
}