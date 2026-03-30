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
    public class DeltaUserReaderFunction
    {
        private readonly ILogger<DeltaUserReaderFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly SGMembershipCalculator _calculator;

        public DeltaUserReaderFunction(ILogger<DeltaUserReaderFunction> logger, IBlobStorageRepository blobStorageRepository, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(DeltaUserReaderFunction))]
        public async Task<DeltaUrls> GetDeltaUsersAsync([ActivityTrigger] DeltaUserReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(DeltaUserReaderFunction));
                var runId = request.SyncJob.RunId.GetValueOrDefault();
                var response = await _calculator.GetFirstDeltaUsersPageAsync(request.ObjectId, runId, request.PageCount);

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

                var fileName = $"/{request.TargetGroupId}/userUploads/{runId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
                await _blobStorageRepository.UploadFileStreamAsync(fileName, response.UsersToAdd, serializerOptions: serializerSettings);
                _logger.FunctionCompleted(nameof(DeltaUserReaderFunction));
                return new DeltaUrls
                {
                    NextPageUrl = response.NextPageUrl,
                    DeltaUrl = response.DeltaUrl
                };
            }
        }
    }
}