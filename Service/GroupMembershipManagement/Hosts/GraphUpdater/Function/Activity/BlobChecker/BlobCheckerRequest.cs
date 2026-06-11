// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.GraphUpdater
{
    public class BlobCheckerRequest
    {
        public Guid RunId { get; set; }
        public string Prefix { get; set; }
    }
}