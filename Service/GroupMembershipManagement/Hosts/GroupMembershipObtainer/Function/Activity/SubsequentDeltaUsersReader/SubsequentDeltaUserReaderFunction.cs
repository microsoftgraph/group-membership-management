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
    public class SubsequentDeltaUserReaderFunction
    {
        private readonly ILogger<SubsequentDeltaUserReaderFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly SGMembershipCalculator _calculator;

        public SubsequentDeltaUserReaderFunction(ILogger<SubsequentDeltaUserReaderFunction> logger, IBlobStorageRepository blobStorageRepository, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(SubsequentDeltaUserReaderFunction))]
        public async Task<DeltaUrls> GetSubsequentDeltaUsersAsync([ActivityTrigger] SubsequentDeltaUserReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(SubsequentDeltaUserReaderFunction));
                var response = await _calculator.GetNextDeltaUsersPagesAsync(request.ObjectId, request.NextPageUrl, request.PageCount);

                if (request.ObjectId != request.TargetGroupId)
                {
                    for (int i = 0; i < response.UsersToAdd.Count; i++)
                    {
                        response.UsersToAdd[i].SourceGroup = request.ObjectId;
                    }
                }

                var serializerSettings = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                };

                var runId = request.SyncJob.RunId.GetValueOrDefault();
                var fileName = $"/{request.TargetGroupId}/userUploads/{runId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
                await _blobStorageRepository.UploadFileStreamAsync(fileName, response.UsersToAdd, serializerOptions: serializerSettings);
                _logger.FunctionCompleted(nameof(SubsequentDeltaUserReaderFunction));
                return new DeltaUrls
                {
                    NextPageUrl = response.NextPageUrl,
                    DeltaUrl = response.DeltaUrl
                };
            }
        }
    }
}