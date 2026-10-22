// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.BlobStorage
{
    /// <summary>One page upload to merge: its name (for diagnostics) and a way to open its content.</summary>
    internal sealed record UserUploadInput(string Name, Func<CancellationToken, Task<Stream>> OpenAsync);

    /// <summary>
    /// Thrown by <see cref="UserUploadMerge.MergeSortedAsync"/> when an input is not in canonical order, for example
    /// a page written by an older release during an in-flight run. Callers fall back to the materializing merge.
    /// </summary>
    internal sealed class UnsortedUserUploadException : Exception
    {
        public UnsortedUserUploadException(string blobName, Guid previous, Guid current)
            : base($"The page upload '{blobName}' is not in canonical order: {current} follows {previous}.")
        {
            BlobName = blobName;
            Previous = previous;
            Current = current;
        }

        public string BlobName { get; }
        public Guid Previous { get; }
        public Guid Current { get; }
    }

    /// <summary>
    /// Streaming k-way merge of the reader page uploads (JSON arrays of <see cref="AzureADUser"/>, each in canonical
    /// order) into one <see cref="GroupMembership"/> document. Memory is one current member and one small read buffer
    /// per input, independent of the member count. Shared with the in-memory test double.
    /// </summary>
    internal static class UserUploadMerge
    {
        // The options of the materializing merge, so each member deserializes to the same object.
        internal static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // Total read-buffer budget across all inputs, and the per-input bounds.
        private const int ReadBufferBudget = 32 * 1024 * 1024;
        private const int MinReadBufferSize = 64 * 1024;
        private const int MaxReadBufferSize = 4 * 1024 * 1024;

        /// <summary>
        /// The <c>OpenRead</c> buffer for each of <paramref name="inputCount"/> inputs, so all open inputs together
        /// stay near a fixed budget instead of the SDK's 4 MiB default each.
        /// </summary>
        internal static int ReadBufferSize(int inputCount) =>
            Math.Clamp(ReadBufferBudget / Math.Max(1, inputCount), MinReadBufferSize, MaxReadBufferSize);

        /// <summary>
        /// Writes the merged document to <paramref name="destination"/>: the bytes of
        /// <c>JsonSerializer.Serialize(groupMembership)</c> for the materializing merge of the same inputs.
        /// </summary>
        /// <returns>The number of unique members written.</returns>
        /// <exception cref="UnsortedUserUploadException">An input is not in canonical order; nothing should be published.</exception>
        internal static Task<int> WriteMergedAsync(
            Stream destination,
            GroupMembership envelope,
            IReadOnlyList<UserUploadInput> inputs,
            CancellationToken cancellationToken) =>
            BlobStorageRepository.WriteGroupMembershipStreamAsync(
                destination,
                envelope,
                MergeSortedAsync(inputs, cancellationToken),
                cancellationToken);

        /// <summary>
        /// Yields the unique members of all inputs in canonical order. Among equal identifiers the record kept is the
        /// first occurrence in input order, then position within the input: exactly the record the materializing
        /// merge keeps. Null elements are skipped.
        /// </summary>
        /// <exception cref="UnsortedUserUploadException">An input is not in canonical order.</exception>
        internal static async IAsyncEnumerable<AzureADUser> MergeSortedAsync(
            IReadOnlyList<UserUploadInput> inputs,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputs);

            var readers = new UserArrayStreamReader[inputs.Count];
            var current = new AzureADUser[inputs.Count];

            // (ObjectId, input index) is a total order over the heap: an input has at most one queued element, and
            // the lower input index wins a tie, which makes the earliest listed occurrence come out first.
            var heap = new PriorityQueue<int, (Guid Id, int Input)>(inputs.Count, HeapKeyComparer.Instance);
            try
            {
                for (var i = 0; i < inputs.Count; i++)
                {
                    var stream = await inputs[i].OpenAsync(cancellationToken)
                        ?? throw new Exception($"Failed to deserialize blob: {inputs[i].Name}");
                    readers[i] = new UserArrayStreamReader(stream, inputs[i].Name);
                    var first = await readers[i].ReadNextAsync(cancellationToken);
                    if (first == null)
                    {
                        await DisposeReaderAsync(readers, i);
                        continue;
                    }

                    current[i] = first;
                    heap.Enqueue(i, (first.ObjectId, i));
                }

                var hasLast = false;
                var lastId = Guid.Empty;
                while (heap.TryDequeue(out var input, out _))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var member = current[input];
                    current[input] = null;
                    if (!hasLast || member.ObjectId != lastId)
                    {
                        hasLast = true;
                        lastId = member.ObjectId;
                        yield return member;
                    }

                    var next = await readers[input].ReadNextAsync(cancellationToken);
                    if (next == null)
                    {
                        await DisposeReaderAsync(readers, input);
                        continue;
                    }

                    if (next.ObjectId.CompareTo(member.ObjectId) < 0)
                    {
                        throw new UnsortedUserUploadException(inputs[input].Name, member.ObjectId, next.ObjectId);
                    }

                    current[input] = next;
                    heap.Enqueue(input, (next.ObjectId, input));
                }
            }
            finally
            {
                for (var i = 0; i < readers.Length; i++)
                {
                    await DisposeReaderAsync(readers, i);
                }
            }
        }

        /// <summary>Logs that a merge fell back to the materializing path because an input was not sorted.</summary>
        internal static void LogUnsortedFallback(ILogger logger, string sourceBlobPrefix, UnsortedUserUploadException exception)
        {
            logger?.LogWarning(
                "The page uploads under {SourceBlobPrefix} are not all in canonical order ({BlobName}: {CurrentObjectId} follows {PreviousObjectId}), " +
                "for example because an older release wrote them. Merging them in memory instead of streaming.",
                sourceBlobPrefix,
                exception.BlobName,
                exception.Current,
                exception.Previous);
        }

        private static async ValueTask DisposeReaderAsync(UserArrayStreamReader[] readers, int index)
        {
            var reader = readers[index];
            if (reader != null)
            {
                readers[index] = null;
                await reader.DisposeAsync();
            }
        }

        private sealed class HeapKeyComparer : IComparer<(Guid Id, int Input)>
        {
            public static readonly HeapKeyComparer Instance = new HeapKeyComparer();

            public int Compare((Guid Id, int Input) x, (Guid Id, int Input) y)
            {
                var byId = x.Id.CompareTo(y.Id);
                return byId != 0 ? byId : x.Input.CompareTo(y.Input);
            }
        }
    }

    /// <summary>
    /// Reads a JSON array of <see cref="AzureADUser"/> one element at a time from a stream, holding only the bytes of
    /// the current element. Accepts what <c>JsonSerializer.DeserializeAsync&lt;List&lt;AzureADUser&gt;&gt;</c> accepts
    /// for the page uploads, with the same options; null elements are skipped.
    /// </summary>
    internal sealed class UserArrayStreamReader : IAsyncDisposable
    {
        private const int InitialBufferSize = 16 * 1024;

        // Reject a single element beyond this size instead of growing without bound.
        private const int MaxBufferSize = 16 * 1024 * 1024;

        private static readonly byte[] _utf8Bom = { 0xEF, 0xBB, 0xBF };

        private readonly Stream _stream;
        private readonly string _name;
        private byte[] _buffer;
        private int _start;
        private int _end;
        private bool _isFinalBlock;
        private bool _bomChecked;
        private JsonReaderState _state;
        private Phase _phase;

        public UserArrayStreamReader(Stream stream, string name)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _name = name;
            _buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
            _state = new JsonReaderState();
        }

        private enum Phase
        {
            BeforeArray,
            InArray,
            AfterArray,
            Done
        }

        /// <summary>Returns the next non-null element, or <see langword="null"/> after the last one.</summary>
        public async ValueTask<AzureADUser> ReadNextAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_phase == Phase.Done)
                {
                    return null;
                }

                AzureADUser user;
                bool hasElement;
                try
                {
                    hasElement = TryReadElement(out user);
                }
                catch (JsonException exception) when (exception.GetType() != typeof(JsonException))
                {
                    // Utf8JsonReader throws a JsonException subtype; surface a JsonException naming the upload, as
                    // the serializer does for the materializing merge.
                    throw new JsonException($"The page upload '{_name}' is not valid JSON. {exception.Message}", exception);
                }

                if (hasElement)
                {
                    if (user != null)
                    {
                        return user;
                    }

                    continue;
                }

                if (_phase == Phase.Done)
                {
                    return null;
                }

                await FillAsync(cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }

            await _stream.DisposeAsync();
        }

        /// <summary>
        /// Parses the buffered bytes. Returns <see langword="true"/> with the next element (null for a JSON null)
        /// when one is complete; <see langword="false"/> when more input is needed or the array has ended.
        /// </summary>
        private bool TryReadElement(out AzureADUser user)
        {
            user = null;
            if (!_bomChecked)
            {
                if (_end - _start < _utf8Bom.Length && !_isFinalBlock)
                {
                    return false;
                }

                if (_buffer.AsSpan(_start, _end - _start).StartsWith(_utf8Bom))
                {
                    _start += _utf8Bom.Length;
                }

                _bomChecked = true;
            }

            var reader = new Utf8JsonReader(_buffer.AsSpan(_start, _end - _start), _isFinalBlock, _state);
            while (true)
            {
                if (!reader.Read())
                {
                    if (_isFinalBlock)
                    {
                        if (_phase == Phase.AfterArray)
                        {
                            _phase = Phase.Done;
                            return false;
                        }

                        // Matches the serializer for an empty or truncated upload.
                        throw new JsonException($"The page upload '{_name}' ended before its JSON array was complete.");
                    }

                    Commit(ref reader);
                    return false;
                }

                switch (_phase)
                {
                    case Phase.BeforeArray:
                        if (reader.TokenType == JsonTokenType.Null)
                        {
                            // The materializing merge rejects a null document with this message.
                            throw new Exception($"Failed to deserialize blob: {_name}");
                        }

                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            throw new JsonException($"The page upload '{_name}' is not a JSON array.");
                        }

                        _phase = Phase.InArray;
                        Commit(ref reader);
                        reader = new Utf8JsonReader(_buffer.AsSpan(_start, _end - _start), _isFinalBlock, _state);
                        continue;

                    case Phase.InArray:
                        switch (reader.TokenType)
                        {
                            case JsonTokenType.EndArray:
                                _phase = Phase.AfterArray;
                                Commit(ref reader);
                                reader = new Utf8JsonReader(_buffer.AsSpan(_start, _end - _start), _isFinalBlock, _state);
                                continue;

                            case JsonTokenType.Null:
                                Commit(ref reader);
                                return true;

                            case JsonTokenType.StartObject:
                                var elementStart = (int)reader.TokenStartIndex;
                                if (!reader.TrySkip())
                                {
                                    // Incomplete element: keep the state from before it and read more.
                                    return false;
                                }

                                var elementEnd = (int)reader.BytesConsumed;
                                user = JsonSerializer.Deserialize<AzureADUser>(
                                    _buffer.AsSpan(_start + elementStart, elementEnd - elementStart),
                                    UserUploadMerge.ReadOptions);
                                Commit(ref reader);
                                return true;

                            default:
                                throw new JsonException($"The page upload '{_name}' contains a {reader.TokenType} element; expected an object.");
                        }

                    case Phase.AfterArray:
                        throw new JsonException($"The page upload '{_name}' has content after its JSON array.");

                    default:
                        return false;
                }
            }
        }

        private void Commit(ref Utf8JsonReader reader)
        {
            _start += (int)reader.BytesConsumed;
            _state = reader.CurrentState;
        }

        private async ValueTask FillAsync(CancellationToken cancellationToken)
        {
            if (_isFinalBlock)
            {
                throw new JsonException($"The page upload '{_name}' ended before its JSON array was complete.");
            }

            var remaining = _end - _start;
            if (_start > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, remaining);
                _start = 0;
                _end = remaining;
            }

            if (_end == _buffer.Length)
            {
                if (_buffer.Length >= MaxBufferSize)
                {
                    throw new JsonException($"An element of the page upload '{_name}' exceeds {MaxBufferSize} bytes.");
                }

                var larger = ArrayPool<byte>.Shared.Rent(Math.Min(_buffer.Length * 2, MaxBufferSize));
                Buffer.BlockCopy(_buffer, 0, larger, 0, _end);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = larger;
            }

            var read = await _stream.ReadAsync(_buffer.AsMemory(_end, _buffer.Length - _end), cancellationToken);
            if (read == 0)
            {
                _isFinalBlock = true;
            }
            else
            {
                _end += read;
            }
        }
    }
}
