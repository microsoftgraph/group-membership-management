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
    public class SubsequentDeltaUserReaderFunction
    {
		private readonly ILoggingRepository _log;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly SGMembershipCalculator _calculator;

		public SubsequentDeltaUserReaderFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[Function(nameof(SubsequentDeltaUserReaderFunction))]
		public async Task<DeltaUrls> GetSubsequentDeltaUsersAsync([ActivityTrigger] SubsequentDeltaUserReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentDeltaUserReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			_calculator.RunId = request.RunId;
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

            var fileName = $"/{request.TargetGroupId}/userUploads/{request.RunId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
            await _blobStorageRepository.UploadFileStreamAsync(fileName, response.UsersToAdd, serializerOptions: serializerSettings);
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentDeltaUserReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return new DeltaUrls
            {
                NextPageUrl = response.NextPageUrl,
                DeltaUrl = response.DeltaUrl
            };
		}
	}
}
