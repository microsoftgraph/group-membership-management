// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class DeltaLinkUserReaderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public DeltaLinkUserReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator, IBlobStorageRepository blobStorageRepository)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(DeltaLinkUserReaderFunction))]
        public async Task<DeltaUrls> GetDeltaLinkUsersAsync([ActivityTrigger] DeltaLinkUserReaderRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaLinkUserReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            _calculator.RunId = request.RunId;
            var response = await _calculator.GetFirstDeltaLinkUsersPageAsync(request.DeltaLink, request.NumberOfPages);

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

            var fileName = $"/{request.TargetGroupId}/userUploads/deltaLink/adds/{request.RunId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonSerializer.Serialize(response.UsersToAdd, serializerSettings));

            var fileNameRemove = $"/{request.TargetGroupId}/userUploads/deltaLink/removes/{request.RunId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
            await _blobStorageRepository.UploadFileAsync(fileNameRemove, JsonSerializer.Serialize(response.UsersToRemove, serializerSettings));

            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaLinkUserReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return new DeltaUrls
            {
                NextPageUrl = response.NextPageUrl,
                DeltaUrl = response.DeltaUrl
            };
        }
    }
}