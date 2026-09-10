// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.DurableTask.Entities;
using System;

namespace Hosts.AgentReader
{
    public static class AgentReaderInstance
    {
        public static string GetId(Guid runId)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(runId, Guid.Empty);
            return $"AgentReader-{runId:D}";
        }

        public static EntityInstanceId GetStartEntityId(Guid runId)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(runId, Guid.Empty);
            return new EntityInstanceId(nameof(AgentReaderStartEntity), runId.ToString("D"));
        }
    }
}
