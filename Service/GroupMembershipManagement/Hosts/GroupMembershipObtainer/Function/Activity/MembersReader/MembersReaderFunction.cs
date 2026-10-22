// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
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
                // FR-020: a page that still fails after the shared retry policies' attempts throws, so the activity
                // fails, nothing is uploaded or logged for this call, and the job reaches TransientError.
                var response = await _calculator.GetFirstTransitiveMembersPageAsync(
                    request.GroupId,
                    runId,
                    request.IdentityTypes);

                // FR-010: every part except the job's destination part carries its source group, including a
                // source part whose group equals the destination. Older requests (null) keep the group comparison.
                var isDestinationPart = request.IsDestinationPart ?? request.GroupId == request.TargetGroupId;
                if (!isDestinationPart)
                {
                    for (int i = 0; i < response.Users.Count; i++)
                    {
                        response.Users[i].SourceGroup = request.GroupId;
                    }
                }

                // The merge streams the page uploads as a k-way merge, so each upload is written in canonical order.
                // Stable, so a member listed twice in one page keeps its first record, as the merge did before.
                CanonicalMemberSort.SortStable(response.Users);

                var serializerSettings = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                };

                var fileName = $"/{request.TargetGroupId}/userUploads/{runId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
                await _blobStorageRepository.UploadFileStreamAsync(fileName, response.Users, serializerOptions: serializerSettings);
                GroupReadLogWriter.ReaderSucceeded(
                    _logger,
                    request.GroupId,
                    response.Counts,
                    request.IdentityTypes,
                    request.ReadKind ?? GroupMembershipReadKind.Full);
                _logger.FunctionCompleted(nameof(MembersReaderFunction));
                return response.NextPageUrl;
            }
        }
    }
}