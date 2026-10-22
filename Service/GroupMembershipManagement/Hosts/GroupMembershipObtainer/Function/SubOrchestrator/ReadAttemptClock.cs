// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Hands out the file timestamp of each read attempt in one sub-orchestrator invocation.
    /// </summary>
    /// <remarks>
    /// Create one instance per orchestrator invocation (a local in the orchestrator function) and pass it down
    /// the read paths. It holds no static or shared state, so every replay episode rebuilds the same sequence
    /// from the replay-safe <c>CurrentUtcDateTime</c> values.
    /// </remarks>
    public sealed class ReadAttemptClock
    {
        private DateTime? _lastTimestamp;

        /// <summary>
        /// Returns the timestamp for a new read attempt: <paramref name="currentUtcDateTime"/> truncated to the
        /// minute used by the blob names, but at least one minute after the previous attempt. Distinct attempts
        /// therefore never share a part-file, cache or delta-link name, so a write-once rejection only ever
        /// means that the same attempt already wrote the blob.
        /// </summary>
        /// <param name="currentUtcDateTime">The orchestration's replay-safe current UTC time.</param>
        public DateTime Next(DateTime currentUtcDateTime)
        {
            var timestamp = new DateTime(
                currentUtcDateTime.Ticks - currentUtcDateTime.Ticks % TimeSpan.TicksPerMinute,
                DateTimeKind.Utc);
            if (_lastTimestamp.HasValue && timestamp <= _lastTimestamp.Value)
            {
                timestamp = _lastTimestamp.Value.AddMinutes(1);
            }

            _lastTimestamp = timestamp;
            return timestamp;
        }
    }
}
