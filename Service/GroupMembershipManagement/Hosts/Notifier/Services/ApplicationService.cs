// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Azure.Core;
using Entities;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Services
{
    public class ApplicationService : IApplicationService
    {
        private readonly ILoggingRepository _loggingRepository;

        public ApplicationService(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository;
        }


        public async Task RunAsync()
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Application service run async." });
        }

    }
}
