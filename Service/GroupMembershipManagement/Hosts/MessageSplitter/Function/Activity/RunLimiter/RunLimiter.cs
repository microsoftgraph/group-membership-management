// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DurableTask.Entities;

namespace Hosts.MessageSplitter
{
    public class RunLimiter : TaskEntity<RunLimiterState>
    {
        [Function(nameof(RunLimiter))]
        public static Task Run([EntityTrigger] TaskEntityDispatcher dispatcher)
        {
            return dispatcher.DispatchAsync<RunLimiter>();
        }

        public AcquireLeaseResponse Acquire(AcquireLeaseRequest request)
        {
            State ??= new RunLimiterState();

            PruneExpired(request.UtcNow);

            var runKey = request.RunId.ToString();

            if (State.Leases.TryGetValue(runKey, out var existingExpiry))
            {
                return new AcquireLeaseResponse(true, State.Leases.Count, existingExpiry);
            }

            if (State.Leases.Count >= request.MaxInFlight)
            {
                return new AcquireLeaseResponse(false, State.Leases.Count, null);
            }

            var expiry = request.UtcNow.AddMinutes(request.LeaseTimeoutMinutes);
            State.Leases[runKey] = expiry;

            return new AcquireLeaseResponse(true, State.Leases.Count, expiry);
        }

        public bool Release(Guid runId)
        {
            State ??= new RunLimiterState();
            var runKey = runId.ToString();

            if (!State.Leases.ContainsKey(runKey))
            {
                return false;
            }

            return State.Leases.Remove(runKey);
        }

        public RenewLeaseResponse Renew(RenewLeaseRequest request)
        {
            State ??= new RunLimiterState();

            PruneExpired(request.UtcNow);

            var runKey = request.RunId.ToString();
            if (!State.Leases.ContainsKey(runKey))
            {
                return new RenewLeaseResponse(false, null);
            }

            var expiry = request.UtcNow.AddMinutes(request.LeaseTimeoutMinutes);
            State.Leases[runKey] = expiry;

            return new RenewLeaseResponse(true, expiry);
        }

        public RunLimiterState GetState()
        {
            State ??= new RunLimiterState();
            return State;
        }

        public int Prune(DateTimeOffset utcNow)
        {
            State ??= new RunLimiterState();

            var before = State.Leases.Count;
            PruneExpired(utcNow);
            return before - State.Leases.Count;
        }

        private void PruneExpired(DateTimeOffset utcNow)
        {
            if (State?.Leases == null || State.Leases.Count == 0)
            {
                return;
            }

            var expiredKeys = new List<string>();
            foreach (var kvp in State.Leases)
            {
                if (kvp.Value <= utcNow)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                State.Leases.Remove(key);
            }
        }
    }
}
