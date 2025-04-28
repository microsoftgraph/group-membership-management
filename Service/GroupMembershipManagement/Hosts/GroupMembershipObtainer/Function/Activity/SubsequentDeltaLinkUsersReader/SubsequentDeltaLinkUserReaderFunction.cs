// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class SubsequentDeltaLinkUserReaderFunction
    {
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public SubsequentDeltaLinkUserReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator, IBlobStorageRepository blobStorageRepository)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

		[FunctionName(nameof(SubsequentDeltaLinkUserReaderFunction))]
		public async Task<DeltaUrls> GetSubsequentDeltaLinkUsersAsync([ActivityTrigger] SubsequentDeltaLinkUserReaderRequest request)
		{
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentDeltaLinkUserReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            _calculator.RunId = request.RunId;
            var response = await _calculator.GetNextDeltaLinkUsersPageAsync(request.NextPageUrl, request.PageCount);

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

            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentDeltaLinkUserReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return new DeltaUrls
            {
                NextPageUrl = response.NextPageUrl,
                DeltaUrl = response.DeltaUrl
            };

        }
	}
}
