// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class TransitiveAndDeltaUsersSenderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly SGMembershipCalculator _calculator;

        public TransitiveAndDeltaUsersSenderFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository;
            _calculator = calculator;
            _blobStorageRepository = blobStorageRepository;
        }

        [FunctionName(nameof(TransitiveAndDeltaUsersSenderFunction))]
        public async Task<string> SendUsersAsync([ActivityTrigger] TransitiveAndDeltaUsersSenderRequest request)
        {
            string filePath = null;

            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(TransitiveAndDeltaUsersSenderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            filePath = await _calculator.SendTransitiveAndDeltaMembershipAsync(request.SyncJob, request.CurrentPart, request.Exclusionary);
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(TransitiveAndDeltaUsersSenderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return filePath;
        }
    }
}