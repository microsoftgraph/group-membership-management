// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Entities;
using Models.Notifications;
using Models.ServiceBus;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Models.Helpers;

namespace Hosts.JobFinalizer
{
    public class JobFinalizerService
    {

        private readonly ILoggingRepository _log;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;


        public SGMembershipCalculator(
                                      IDatabaseSyncJobsRepository databaseSyncJobsRepository,
                                      ILoggingRepository logging,
                                      )
        {
            _log = logging;
            _databaseSyncJobsRepository = databaseSyncJobsRepository;
        }

        public async Task UpdateSyncJobStatusAsync(SyncJob job, SyncStatus status)
        {
            await _databaseSyncJobsRepository.UpdateSyncJobStatusAsync(new[] { job }, status);
        }

    }
}