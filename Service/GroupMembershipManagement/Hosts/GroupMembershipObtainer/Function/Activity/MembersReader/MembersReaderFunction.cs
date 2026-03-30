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
    public class MembersReaderFunction
    {
        private readonly ILogger<MembersReaderFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly SGMembershipCalculator _calculator;

        public MembersReaderFunction(ILogger<MembersReaderFunction> logger, IBlobStorageRepository blobStorageRepository, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(MembersReaderFunction))]
        public async Task<string> GetMembersAsync([ActivityTrigger] MembersReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(MembersReaderFunction));
                var runId = request.SyncJob.RunId.GetValueOrDefault();
                var response = await _calculator.GetFirstTransitiveMembersPageAsync(request.GroupId, runId);

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

                var fileName = $"/{request.TargetGroupId}/userUploads/{runId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
                await _blobStorageRepository.UploadFileStreamAsync(fileName, response.Users, serializerOptions: serializerSettings);
                _logger.FunctionCompleted(nameof(MembersReaderFunction));
                return response.NextPageUrl;
            }
        }
    }
}