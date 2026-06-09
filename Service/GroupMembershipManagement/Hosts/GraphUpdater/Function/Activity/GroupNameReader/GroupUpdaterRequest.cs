// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Services.Entities;
using System;

namespace Hosts.GraphUpdater
{
    public class GroupNameReaderRequest : GraphUpdaterRequestBase
    {
        public Guid GroupId { get; set; }
    }
}