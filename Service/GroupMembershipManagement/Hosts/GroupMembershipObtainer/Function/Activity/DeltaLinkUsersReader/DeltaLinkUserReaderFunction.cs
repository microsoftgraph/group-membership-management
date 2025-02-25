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
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class DeltaLinkUserReaderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;

        public DeltaLinkUserReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [FunctionName(nameof(DeltaLinkUserReaderFunction))]
        public async Task<DeltaGroupInformation> GetDeltaLinkUsersAsync([ActivityTrigger] DeltaLinkUserReaderRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaLinkUserReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            _calculator.RunId = request.RunId;
            var response = await _calculator.GetFirstDeltaLinkUsersPageAsync(request.DeltaLink);
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaLinkUserReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return response;

        }
    }
}