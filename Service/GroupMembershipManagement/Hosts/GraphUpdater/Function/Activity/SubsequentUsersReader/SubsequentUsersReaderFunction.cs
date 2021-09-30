// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class SubsequentUsersReaderFunction
    {
		private readonly ILoggingRepository _loggingRepository;
		private readonly IGraphUpdaterService _usersReaderService;

		public SubsequentUsersReaderFunction(ILoggingRepository loggingRepository, IGraphUpdaterService usersReaderService)
		{
			_loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_usersReaderService = usersReaderService ?? throw new ArgumentNullException(nameof(usersReaderService));
		}

		[FunctionName(nameof(SubsequentUsersReaderFunction))]
		public async Task<UsersPageResponse> GetUsersAsync([ActivityTrigger] SubsequentUsersReaderRequest request)
		{
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentUsersReaderFunction)} function started", RunId = request.RunId });
			var response = await _usersReaderService.GetNextMembersPageAsync(request.NextPageUrl, request.GroupMembersPage, request.RunId);
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentUsersReaderFunction)} function completed", RunId = request.RunId });
			return response;
		}
	}
}
