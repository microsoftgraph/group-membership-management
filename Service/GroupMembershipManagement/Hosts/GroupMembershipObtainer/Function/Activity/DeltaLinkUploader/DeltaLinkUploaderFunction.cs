// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class DeltaLinkUploaderFunction
    {
        private readonly ILogger<DeltaLinkUploaderFunction> _logger;
        private readonly SGMembershipCalculator _calculator;

        public DeltaLinkUploaderFunction(ILogger<DeltaLinkUploaderFunction> logger, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(DeltaLinkUploaderFunction))]
        public async Task SendDeltaLinkAsync([ActivityTrigger] DeltaLinkUploaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(DeltaLinkUploaderFunction));
                await _calculator.UploadDeltaLinkAsync(request.ObjectId, request.DeltaLink, request.SyncJob.RunId.GetValueOrDefault());
                _logger.FunctionCompleted(nameof(DeltaLinkUploaderFunction));
            }
        }
    }
}