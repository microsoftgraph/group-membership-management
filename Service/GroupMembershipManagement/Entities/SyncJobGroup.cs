// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class SyncJobGroup
    {
        public SyncJob SyncJob { get; set; }
        public string Name { get; set; }
    }
}