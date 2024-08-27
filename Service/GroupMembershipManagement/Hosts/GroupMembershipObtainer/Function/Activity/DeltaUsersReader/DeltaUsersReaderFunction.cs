// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.GroupMembershipObtainer
{
    public class DeltaUsersReaderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;

        public DeltaUsersReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(DeltaUsersReaderFunction))]
        public async Task<DeltaGroupInformation> GetDeltaUsersAsync([ActivityTrigger] DeltaUsersReaderRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaUsersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            _calculator.RunId = request.RunId;
            var response = await _calculator.GetFirstDeltaUsersPageAsync(request.DeltaLink);
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaUsersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return response;

        }
    }
}