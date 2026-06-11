// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class TransitiveAndDeltaUsersSenderFunction
    {
        private readonly ILogger<TransitiveAndDeltaUsersSenderFunction> _logger;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly SGMembershipCalculator _calculator;

        public TransitiveAndDeltaUsersSenderFunction(ILogger<TransitiveAndDeltaUsersSenderFunction> logger, IBlobStorageRepository blobStorageRepository, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator;
            _blobStorageRepository = blobStorageRepository;
        }

        [Function(nameof(TransitiveAndDeltaUsersSenderFunction))]
        public async Task<GroupMembershipFileResult> SendUsersAsync([ActivityTrigger] TransitiveAndDeltaUsersSenderRequest request)
        {
            GroupMembershipFileResult membershipFileResult = null;

            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(TransitiveAndDeltaUsersSenderFunction));
                membershipFileResult = await _calculator.SendTransitiveAndDeltaMembershipAsync(request.SyncJob, request.ObjectId, request.CurrentPart, request.Exclusionary);
                _logger.FunctionCompleted(nameof(TransitiveAndDeltaUsersSenderFunction));
                return membershipFileResult;
            }
        }
    }
}