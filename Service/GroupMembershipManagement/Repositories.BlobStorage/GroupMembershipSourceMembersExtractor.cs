// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Repositories.BlobStorage
{
    /// <summary>
    /// Synchronous streaming extractor for SourceMembers[].ObjectId GUIDs from a GroupMembership JSON blob.
    /// Supports:
    ///   1. { "ObjectId": "guid" } elements
    ///   2. "guid" raw string elements
    /// </summary>
    public static class GroupMembershipSourceMembersStreamingExtractor
    {
        /// <summary>
        /// Synchronously parses the stream and returns distinct GUIDs.
        /// Caller is responsible for providing a readable stream positioned at start.
        /// </summary>
        public static HashSet<Guid> Extract(Stream jsonStream, int bufferSize = 64 * 1024, CancellationToken cancellationToken = default)
        {
            if (jsonStream == null) throw new ArgumentNullException(nameof(jsonStream));
            if (!jsonStream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(jsonStream));
            if (bufferSize < 4096) bufferSize = 4096;
            var ids = new HashSet<Guid>();

            foreach (var guid in EnumerateGuids(jsonStream, bufferSize, cancellationToken))
            {
                ids.Add(guid);
            }

            return ids;
        }

        /// <summary>
        /// Streaming enumerable that yields GUIDs one at a time without accumulating them in memory.
        /// Use this for writing directly to a cache file.
        /// Note: This does NOT deduplicate - if the source contains duplicates, they will be yielded.
        /// For deduplicated results, use Extract() instead.
        /// </summary>
        public static IEnumerable<Guid> EnumerateGuids(Stream jsonStream, int bufferSize = 64 * 1024, CancellationToken cancellationToken = default)
        {
            if (jsonStream == null) throw new ArgumentNullException(nameof(jsonStream));
            if (!jsonStream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(jsonStream));
            if (bufferSize < 4096) bufferSize = 4096;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            // Utf8JsonReader state across chunks
            JsonReaderState readerState = default;

            // Extraction state
            bool insideSourceMembers = false;
            int depthInsideSourceMembers = 0; // how many nested objects deep inside SourceMembers array
            string? currentProperty = null;
            bool lastPropertyWasSourceMembers = false;

            int bytesInBuffer = 0;          // total valid bytes currently in buffer (including leftover from previous chunk)
            bool isFinalBlock = false;      // set only after we get 0 bytes from stream

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!isFinalBlock)
                    {
                        // Fill remaining space (append after any leftover)
                        int read = jsonStream.Read(buffer, bytesInBuffer, buffer.Length - bytesInBuffer);
                        if (read == 0)
                        {
                            isFinalBlock = true; // no more data
                        }
                        else
                        {
                            bytesInBuffer += read;
                        }
                    }

                    var span = new ReadOnlySpan<byte>(buffer, 0, bytesInBuffer);
                    var reader = new Utf8JsonReader(span, isFinalBlock, readerState);

                    // Collect GUIDs found in this chunk to yield after updating reader state
                    var guidsToYield = new List<Guid>();

                    try
                    {
                        while (reader.Read())
                        {
                            switch (reader.TokenType)
                            {
                                case JsonTokenType.PropertyName:
                                    currentProperty = reader.GetString();
                                    if (!insideSourceMembers && string.Equals(currentProperty, "SourceMembers", StringComparison.OrdinalIgnoreCase))
                                        lastPropertyWasSourceMembers = true;
                                    break;

                                case JsonTokenType.StartArray:
                                    if (lastPropertyWasSourceMembers)
                                    {
                                        insideSourceMembers = true;
                                        depthInsideSourceMembers = 0;
                                        lastPropertyWasSourceMembers = false;
                                    }
                                    break;

                                case JsonTokenType.EndArray:
                                    if (insideSourceMembers && depthInsideSourceMembers == 0)
                                        insideSourceMembers = false;
                                    break;

                                case JsonTokenType.StartObject:
                                    if (insideSourceMembers)
                                        depthInsideSourceMembers++;
                                    break;

                                case JsonTokenType.EndObject:
                                    if (insideSourceMembers && depthInsideSourceMembers > 0)
                                        depthInsideSourceMembers--;
                                    break;

                                case JsonTokenType.String:
                                    if (!insideSourceMembers)
                                        break;

                                    bool isObjectForm = depthInsideSourceMembers > 0 &&
                                        currentProperty != null &&
                                        currentProperty.Equals("ObjectId", StringComparison.OrdinalIgnoreCase);

                                    bool isRawGuidElement = depthInsideSourceMembers == 0; // element directly in array

                                    if (isObjectForm || isRawGuidElement)
                                    {
                                        var s = reader.GetString();
                                        if (!string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out var g))
                                        {
                                            guidsToYield.Add(g);
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        throw new JsonException($"Streaming parse failed (finalBlock={isFinalBlock}, bytesInBuffer={bytesInBuffer}, bytesConsumed={reader.BytesConsumed}). {ex.Message}", ex);
                    }

                    // Determine how many bytes were consumed; shift leftover to beginning
                    long consumed = reader.BytesConsumed;
                    int remaining = bytesInBuffer - (int)consumed;
                    if (remaining > 0)
                    {
                        Buffer.BlockCopy(buffer, (int)consumed, buffer, 0, remaining);
                    }
                    bytesInBuffer = remaining;
                    readerState = reader.CurrentState;

                    // Yield GUIDs found in this chunk
                    foreach (var guid in guidsToYield)
                    {
                        yield return guid;
                    }

                    if (isFinalBlock)
                        break; // done
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}