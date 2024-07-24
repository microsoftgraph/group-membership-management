// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Entities
{
    public class ServiceStatusHistory
    {
        public Guid Id { get; set; }
        public Guid ServiceStatusId { get; set; }
        public Guid RequestorObjectId { get; set; }
        public DateTime Timestamp { get; set; }
        public ServiceStatus StatusDetails { get; set; }

    }
}
