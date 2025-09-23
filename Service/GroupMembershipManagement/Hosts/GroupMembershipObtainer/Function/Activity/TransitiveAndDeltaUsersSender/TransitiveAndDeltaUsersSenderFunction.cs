// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
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

        [Function(nameof(TransitiveAndDeltaUsersSenderFunction))]
        public async Task<GroupMembershipFileResult> SendUsersAsync([ActivityTrigger] TransitiveAndDeltaUsersSenderRequest request)
        {
            GroupMembershipFileResult membershipFileResult = null;

            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(TransitiveAndDeltaUsersSenderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            membershipFileResult = await _calculator.SendTransitiveAndDeltaMembershipAsync(request.SyncJob, request.ObjectId, request.CurrentPart, request.Exclusionary);
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(TransitiveAndDeltaUsersSenderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            return membershipFileResult;
        }
    }
}