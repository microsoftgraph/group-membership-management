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
    public class DeltaUserReaderFunction
    {
		private readonly ILoggingRepository _log;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly SGMembershipCalculator _calculator;

		public DeltaUserReaderFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[Function(nameof(DeltaUserReaderFunction))]
		public async Task<DeltaUrls> GetDeltaUsersAsync([ActivityTrigger] DeltaUserReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaUserReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			_calculator.RunId = request.RunId;
            var response = await _calculator.GetFirstDeltaUsersPageAsync(request.ObjectId, request.RunId, request.PageCount);

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

            var fileName = $"/{request.TargetGroupId}/userUploads/{request.RunId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonSerializer.Serialize(response.UsersToAdd, serializerSettings));
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaUserReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return new DeltaUrls
            {
                NextPageUrl = response.NextPageUrl,
                DeltaUrl = response.DeltaUrl
            };
        }
	}
}
