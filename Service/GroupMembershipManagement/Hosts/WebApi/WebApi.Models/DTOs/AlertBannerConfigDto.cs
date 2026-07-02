// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Text.Json.Serialization;

namespace WebApi.Models.DTOs
{
    /// <summary>
    /// Represents the configuration for the GMM UI alert banner.
    /// Stored as a single JSON value in the <c>Setting</c> row keyed by
    /// <see cref="Models.SettingKey.AlertBannerConfig"/>.
    /// All dates are treated as UTC (ISO 8601) end-to-end.
    /// </summary>
    public class AlertBannerConfigDto
    {
        /// <summary>The alert message rendered to users. Plain text only.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Whether the alert banner is enabled.</summary>
        public bool IsEnabled { get; set; }

        /// <summary>UTC start of the visibility window (inclusive).</summary>
        public DateTime StartDate { get; set; }

        /// <summary>UTC end of the visibility window (exclusive).</summary>
        public DateTime EndDate { get; set; }

        /// <summary>Optional https-only link URL displayed with the message.</summary>
        public string? LinkUrl { get; set; }

        /// <summary>Optional display text for <see cref="LinkUrl"/>.</summary>
        public string? LinkText { get; set; }

        /// <summary>
        /// Normalizes all <see cref="DateTime"/> values to <see cref="DateTimeKind.Utc"/>.
        /// JSON round-trips can yield <see cref="DateTimeKind.Unspecified"/>; this keeps
        /// the contract UTC-consistent.
        /// </summary>
        public void NormalizeDatesToUtc()
        {
            StartDate = ToUtc(StartDate);
            EndDate = ToUtc(EndDate);
        }

        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        /// <summary>
        /// Returns the default (disabled) configuration used when no setting row exists.
        /// </summary>
        [JsonIgnore]
        public static AlertBannerConfigDto Default
        {
            get
            {
                var now = DateTime.UtcNow;
                return new AlertBannerConfigDto
                {
                    Message = string.Empty,
                    IsEnabled = false,
                    StartDate = now,
                    EndDate = now,
                    LinkUrl = null,
                    LinkText = null
                };
            }
        }
    }
}
