// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.GraphUpdater
{
    public class GroupOwnersReaderRequest
    {
        public Guid RunId { get; set; }
        public Guid GroupId { get; set; }
    }
}