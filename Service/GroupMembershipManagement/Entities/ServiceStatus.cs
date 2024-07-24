// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;

namespace Entities
{
    public class ServiceStatus
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public ServiceStatuses Status { get; set; }
    }
}
