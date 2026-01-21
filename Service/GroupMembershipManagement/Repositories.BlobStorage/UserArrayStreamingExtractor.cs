// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Repositories.BlobStorage
{
    /// <summary>
    /// Synchronous streaming extractor for AzureADUser objects from a JSON array blob.
    /// The input format is a simple array: [ {user1}, {user2}, ... ]
    /// This is used to parse blobs created by MembersReaderFunction, SubsequentMembersReaderFunction, etc.
    /// </summary>
    public static class UserArrayStreamingExtractor
    {
        /// <summary>
        /// Streaming enumerable that yields AzureADUser objects one at a time without accumulating them in memory.
        /// Each user object is captured as raw JSON bytes and then deserialized individually.
        /// </summary>
        public static IEnumerable<AzureADUser> EnumerateUsers(Stream jsonStream, int bufferSize = 64 * 1024, CancellationToken cancellationToken = default)
        {
            if (jsonStream == null) throw new ArgumentNullException(nameof(jsonStream));
            if (!jsonStream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(jsonStream));
            if (bufferSize < 4096) bufferSize = 4096;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            JsonReaderState readerState = default;
            int arrayDepth = 0;           // Track array nesting depth
            int objectDepth = 0;          // Track object nesting depth within current user
            bool insideUserObject = false;
            int userObjectStartIndex = 0; // Position where current user object started
            var userJsonBuilder = new List<byte>(); // Accumulates bytes for current user object

            int bytesInBuffer = 0;
            bool isFinalBlock = false;

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!isFinalBlock)
                    {
                        int read = jsonStream.Read(buffer, bytesInBuffer, buffer.Length - bytesInBuffer);
                        if (read == 0)
                        {
                            isFinalBlock = true;
                        }
                        else
                        {
                            bytesInBuffer += read;
                        }
                    }

                    var span = new ReadOnlySpan<byte>(buffer, 0, bytesInBuffer);
                    var reader = new Utf8JsonReader(span, isFinalBlock, readerState);

                    var usersToYield = new List<AzureADUser>();

                    try
                    {
                        while (reader.Read())
                        {
                            switch (reader.TokenType)
                            {
                                case JsonTokenType.StartArray:
                                    arrayDepth++;
                                    break;

                                case JsonTokenType.EndArray:
                                    arrayDepth--;
                                    break;

                                case JsonTokenType.StartObject:
                                    if (arrayDepth == 1 && !insideUserObject)
                                    {
                                        // Starting a new user object at root array level
                                        insideUserObject = true;
                                        objectDepth = 1;
                                        userObjectStartIndex = (int)reader.TokenStartIndex;
                                        userJsonBuilder.Clear();
                                    }
                                    else if (insideUserObject)
                                    {
                                        objectDepth++;
                                    }
                                    break;

                                case JsonTokenType.EndObject:
                                    if (insideUserObject)
                                    {
                                        objectDepth--;
                                        if (objectDepth == 0)
                                        {
                                            // End of user object - extract and deserialize
                                            // Append bytes from current chunk: from userObjectStartIndex to BytesConsumed
                                            // For single-chunk objects: userObjectStartIndex is the start position
                                            // For multi-chunk objects: userObjectStartIndex was reset to 0, and previous
                                            // chunks' bytes are already in userJsonBuilder
                                            int endIndex = (int)reader.BytesConsumed;
                                            int startPos = Math.Max(0, userObjectStartIndex);

                                            if (startPos < endIndex && startPos < bytesInBuffer)
                                            {
                                                int bytesToAppend = Math.Min(endIndex - startPos, bytesInBuffer - startPos);
                                                for (int i = startPos; i < startPos + bytesToAppend; i++)
                                                {
                                                    userJsonBuilder.Add(buffer[i]);
                                                }
                                            }

                                            // Deserialize the user
                                            var userBytes = userJsonBuilder.ToArray();
                                            var user = JsonSerializer.Deserialize<AzureADUser>(userBytes);
                                            if (user != null)
                                            {
                                                usersToYield.Add(user);
                                            }

                                            insideUserObject = false;
                                            userJsonBuilder.Clear();
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        throw new JsonException($"Streaming user parse failed (finalBlock={isFinalBlock}, bytesInBuffer={bytesInBuffer}, bytesConsumed={reader.BytesConsumed}). {ex.Message}", ex);
                    }

                    // If we're in the middle of a user object that spans chunks, save the bytes
                    if (insideUserObject && !isFinalBlock)
                    {
                        int startPos = Math.Max(0, userObjectStartIndex);
                        int endPos = (int)reader.BytesConsumed;
                        for (int i = startPos; i < endPos && i < bytesInBuffer; i++)
                        {
                            userJsonBuilder.Add(buffer[i]);
                        }
                        userObjectStartIndex = 0; // Reset for next chunk
                    }

                    // Shift leftover bytes
                    long consumed = reader.BytesConsumed;
                    int remaining = bytesInBuffer - (int)consumed;
                    if (remaining > 0)
                    {
                        Buffer.BlockCopy(buffer, (int)consumed, buffer, 0, remaining);
                    }
                    bytesInBuffer = remaining;
                    readerState = reader.CurrentState;

                    // Yield users found in this chunk
                    foreach (var user in usersToYield)
                    {
                        yield return user;
                    }

                    if (isFinalBlock)
                        break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }
}
