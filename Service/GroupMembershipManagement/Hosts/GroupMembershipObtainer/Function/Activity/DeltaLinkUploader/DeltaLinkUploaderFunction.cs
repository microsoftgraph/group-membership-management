// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class DeltaLinkUploaderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;

        public DeltaLinkUploaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(DeltaLinkUploaderFunction))]
        public async Task SendDeltaLinkAsync([ActivityTrigger] DeltaLinkUploaderRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaLinkUploaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);                       
            await _calculator.UploadDeltaLinkAsync(request.ObjectId, request.DeltaLink, request.RunId);           
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaLinkUploaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}