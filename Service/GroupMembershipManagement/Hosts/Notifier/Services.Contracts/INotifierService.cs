// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface INotifierService
    {
        public Task SendEmailAsync(string recipientAddresses);
    }
}
