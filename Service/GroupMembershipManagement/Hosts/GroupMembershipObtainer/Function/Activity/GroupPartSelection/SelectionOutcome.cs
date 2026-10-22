#nullable enable
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Text.Json.Serialization;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// The status of one selection-pipeline activity call. The sub-orchestrator branches only on it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SelectionOutcomeStatus>))]
    public enum SelectionOutcomeStatus
    {
        Completed,

        /// <summary>
        /// The lookup still failed (429, 500/502/503/504, or a transport failure) after the shared retry policies'
        /// configured attempts. The part fails without a fallback read and the job reaches TransientError.
        /// </summary>
        RetriesExhausted,
        LookupUnavailable,
        Failed,
    }

    /// <summary>
    /// A validated activity outcome recorded in Durable history. Each status carries exactly its own detail:
    /// <see cref="HttpStatus"/> only for <see cref="SelectionOutcomeStatus.LookupUnavailable"/>, and
    /// <see cref="Reason"/> only for <see cref="SelectionOutcomeStatus.Failed"/> and
    /// <see cref="SelectionOutcomeStatus.RetriesExhausted"/>. Any other combination is rejected at construction,
    /// including when the outcome is deserialized.
    /// </summary>
    public sealed class SelectionOutcome : IEquatable<SelectionOutcome>
    {
        [JsonConstructor]
        private SelectionOutcome(
            SelectionOutcomeStatus status,
            int? httpStatus,
            string? reason)
        {
            Validate(status, httpStatus, reason);
            Status = status;
            HttpStatus = httpStatus;
            Reason = reason;
        }

        public SelectionOutcomeStatus Status { get; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? HttpStatus { get; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Reason { get; }

        public static SelectionOutcome Completed() =>
            new(SelectionOutcomeStatus.Completed, null, null);

        public static SelectionOutcome RetriesExhausted(string reason) =>
            new(SelectionOutcomeStatus.RetriesExhausted, null, reason);

        public static SelectionOutcome LookupUnavailable(int httpStatus) =>
            new(SelectionOutcomeStatus.LookupUnavailable, httpStatus, null);

        public static SelectionOutcome Failed(string reason) =>
            new(SelectionOutcomeStatus.Failed, null, reason);

        public bool Equals(SelectionOutcome? other) =>
            other is not null &&
            Status == other.Status &&
            HttpStatus == other.HttpStatus &&
            string.Equals(Reason, other.Reason, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as SelectionOutcome);

        public override int GetHashCode() => HashCode.Combine(Status, HttpStatus, Reason);

        public override string ToString() => Status switch
        {
            SelectionOutcomeStatus.RetriesExhausted => $"RetriesExhausted: {Reason}",
            SelectionOutcomeStatus.LookupUnavailable => $"LookupUnavailable ({HttpStatus})",
            SelectionOutcomeStatus.Failed => $"Failed: {Reason}",
            _ => Status.ToString(),
        };

        private static void Validate(
            SelectionOutcomeStatus status,
            int? httpStatus,
            string? reason)
        {
            if (!Enum.IsDefined(status))
            {
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown selection outcome status.");
            }

            if (httpStatus.HasValue != (status == SelectionOutcomeStatus.LookupUnavailable))
            {
                throw new ArgumentException(
                    $"{nameof(HttpStatus)} is required for, and only allowed with, {SelectionOutcomeStatus.LookupUnavailable}.",
                    nameof(httpStatus));
            }

            if (httpStatus.HasValue && (httpStatus.Value < 100 || httpStatus.Value > 599))
            {
                throw new ArgumentOutOfRangeException(nameof(httpStatus), httpStatus, "An HTTP status must be between 100 and 599.");
            }

            var takesReason = status == SelectionOutcomeStatus.Failed || status == SelectionOutcomeStatus.RetriesExhausted;
            if ((reason != null) != takesReason)
            {
                throw new ArgumentException(
                    $"{nameof(Reason)} is required for, and only allowed with, {SelectionOutcomeStatus.Failed} and {SelectionOutcomeStatus.RetriesExhausted}.",
                    nameof(reason));
            }

            if (reason != null && string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("A failure reason cannot be blank.", nameof(reason));
            }
        }
    }
}
