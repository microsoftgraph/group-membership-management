// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using System.Collections.Generic;
using System.Threading.Tasks;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Linq;
using Newtonsoft.Json;
using Repositories.Contracts.InjectConfig;
using Azure;
using Models.Entities;

namespace Services
{
    public class NotifierService : INotifierService
    {
        private readonly ILoggingRepository _loggingRepository;

        public NotifierService(
            ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository;
        }

    }
}
