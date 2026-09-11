// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Repositories.Contracts.DestinationResolution
{
    /// <summary>
    /// Generates the legacy <c>SyncJobs.Destination</c> JSON from a typed
    /// <see cref="ResolvedDestination"/> as a derived compatibility value (FR-018/FR-019,
    /// contract: resolver-json-cutover.md Section JSON compatibility). The output preserves the
    /// existing single-element array wrapper, the <c>GroupMembership</c> /
    /// <c>TeamsChannelMembership</c> type strings, and the lower-case <c>objectId</c> /
    /// <c>channelId</c> field names; <c>channelId</c> is omitted for groups and required for
    /// Teams channels. The JSON is never treated as consolidated-read authority.
    /// </summary>
    public static class DestinationJson
    {
        /// <summary>
        /// Produces the compatibility JSON for the supplied resolved destination.
        /// </summary>
        /// <exception cref="ArgumentNullException">The destination is null.</exception>
        /// <exception cref="ArgumentException">The destination type is unsupported, or a
        /// Teams-channel destination has a missing/empty channel identifier.</exception>
        public static string Generate(ResolvedDestination destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            switch (destination)
            {
                case ResolvedGroupDestination group:
                    return Write(MembershipTypes.GroupMembership.ToString(), group.ObjectId, channelId: null);

                case ResolvedTeamsChannelDestination channel:
                    if (string.IsNullOrEmpty(channel.ChannelId))
                    {
                        throw new ArgumentException(
                            "A Teams-channel destination requires a non-empty channel identifier.", nameof(destination));
                    }

                    return Write(MembershipTypes.TeamsChannelMembership.ToString(), channel.TeamObjectId, channel.ChannelId);

                default:
                    throw new ArgumentException(
                        $"Unsupported destination type '{destination.GetType().Name}'.", nameof(destination));
            }
        }

        private static string Write(string type, Guid objectId, string channelId)
        {
            var buffer = new ArrayBufferWriter<byte>();

            // Compact (no whitespace), matching the existing on-disk representation.
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartArray();
                writer.WriteStartObject();

                writer.WriteString("type", type);

                writer.WritePropertyName("value");
                writer.WriteStartObject();
                writer.WriteString("objectId", objectId);
                if (channelId != null)
                {
                    writer.WriteString("channelId", channelId);
                }
                writer.WriteEndObject();

                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }
}
