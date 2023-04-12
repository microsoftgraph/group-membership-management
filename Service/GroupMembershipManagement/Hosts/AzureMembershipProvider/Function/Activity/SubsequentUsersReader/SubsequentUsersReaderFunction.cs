// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services;
using System;
using System.Threading.Tasks;

namespace Hosts.AzureMembershipProvider
{
    public class SubsequentUsersReaderFunction
	{
		private readonly ILoggingRepository _log;
        private readonly AzureMembershipProviderService _membershipProviderService;

        public SubsequentUsersReaderFunction(ILoggingRepository loggingRepository, AzureMembershipProviderService membershipProviderService)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _membershipProviderService = membershipProviderService ?? throw new ArgumentNullException(nameof(membershipProviderService));
        }

        [FunctionName(nameof(SubsequentUsersReaderFunction))]
		public async Task<UserInformation> GetUsersAsync([ActivityTrigger] SubsequentUsersReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentUsersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			var response = await _membershipProviderService.GetNextUsersAsync(request.NextPageUrl);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentUsersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
