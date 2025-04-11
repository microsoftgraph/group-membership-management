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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class MembersReaderFunction
	{
		private readonly ILoggingRepository _log;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly SGMembershipCalculator _calculator;

		public MembersReaderFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

        [FunctionName(nameof(MembersReaderFunction))]
        public async Task<string> GetMembersAsync([ActivityTrigger] MembersReaderRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(MembersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            _calculator.RunId = request.RunId;
            var response = await _calculator.GetFirstTransitiveMembersPageAsync(request.GroupId, request.RunId);

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

            var fileName = $"/{request.TargetGroupId}/userUploads/{request.RunId}_GroupMembership_{request.CurrentPart}_{Guid.NewGuid()}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonSerializer.Serialize(response.Users, serializerSettings));
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(MembersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return response.NextPageUrl;
        }
    }
}
