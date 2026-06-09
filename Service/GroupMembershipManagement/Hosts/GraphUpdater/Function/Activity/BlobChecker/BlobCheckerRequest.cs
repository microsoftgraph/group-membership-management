// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Services.Entities;

namespace Hosts.GraphUpdater
{
    public class BlobCheckerRequest : GraphUpdaterRequestBase
    {
        public string Prefix { get; set; }
    }
}