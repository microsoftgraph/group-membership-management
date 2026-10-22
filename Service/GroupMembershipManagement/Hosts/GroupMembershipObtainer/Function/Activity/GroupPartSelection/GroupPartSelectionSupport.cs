// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Kiota.Abstractions;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>Member classification codes held in the compact per-member arrays (one byte per member).</summary>
    internal static class MemberTypeCode
    {
        public const byte Unknown = 0;
        public const byte User = 1;
        public const byte AgentUser = 2;
        public const byte NotIdentified = 3;

        public const string UserName = "user";
        public const string AgentUserName = "agentUser";
        public const string UnknownName = "unknown";
        public const string NotIdentifiedName = "notIdentified";

        public static string ToName(byte code) => code switch
        {
            User => UserName,
            AgentUser => AgentUserName,
            NotIdentified => NotIdentifiedName,
            _ => UnknownName,
        };

        public static bool TryParse(string name, out byte code)
        {
            switch (name)
            {
                case UserName: code = User; return true;
                case AgentUserName: code = AgentUser; return true;
                case UnknownName: code = Unknown; return true;
                case NotIdentifiedName: code = NotIdentified; return true;
                default: code = Unknown; return false;
            }
        }

        /// <summary>Maps a Graph <c>@odata.type</c> (with or without prefix) to user or agentUser.</summary>
        public static bool TryFromGraphType(string graphType, out byte code)
        {
            code = Unknown;
            if (string.IsNullOrWhiteSpace(graphType))
            {
                return false;
            }

            var candidate = graphType.Trim().TrimStart('#');
            const string graphPrefix = "microsoft.graph.";
            if (candidate.StartsWith(graphPrefix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring(graphPrefix.Length);
            }

            if (string.Equals(candidate, UserName, StringComparison.OrdinalIgnoreCase))
            {
                code = User;
                return true;
            }

            if (string.Equals(candidate, AgentUserName, StringComparison.OrdinalIgnoreCase))
            {
                code = AgentUser;
                return true;
            }

            return false;
        }
    }

    /// <summary>The write-once manifest that selects one prepare execution's snapshot.</summary>
    internal sealed class SelectionManifest
    {
        [JsonPropertyName("executionId")]
        public Guid ExecutionId { get; set; }

        [JsonPropertyName("stagingCount")]
        public int StagingCount { get; set; }

        [JsonPropertyName("segmentCount")]
        public int SegmentCount { get; set; }

        [JsonPropertyName("unknownSegments")]
        public List<int> UnknownSegments { get; set; } = new List<int>();

        public string Serialize() => JsonSerializer.Serialize(this);

        public static SelectionManifest Parse(string content)
        {
            var manifest = JsonSerializer.Deserialize<SelectionManifest>(content)
                ?? throw new InvalidDataException("The selection manifest is empty.");
            if (manifest.ExecutionId == Guid.Empty || manifest.StagingCount < 0 || manifest.SegmentCount < 0)
            {
                throw new InvalidDataException("The selection manifest is invalid.");
            }

            manifest.UnknownSegments ??= new List<int>();
            return manifest;
        }

        public GroupPartPrepareResult ToResult() => new GroupPartPrepareResult
        {
            Outcome = SelectionOutcome.Completed(),
            ExecutionId = ExecutionId,
            StagingCount = StagingCount,
            SegmentCount = SegmentCount,
            UnknownSegments = new List<int>(UnknownSegments)
        };
    }

    /// <summary>One <c>{"id":...,"type":...}</c> line of a receipt or of the recorded member types.</summary>
    internal sealed class MemberTypeLine
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    /// <summary>Encodes and decodes the per-run snapshot artifacts.</summary>
    internal static class SelectionSnapshotCodec
    {
        public const int SegmentSize = 1000;
        public const string RecordSchemaLine = "{\"schema\":1}";

        /// <summary>Segment lines: <c>{id},{user|agentUser|unknown}</c>, one per staging identifier, ascending.</summary>
        public static string FormatSegment(Guid[] ids, byte[] types, int start, int count)
        {
            var builder = new StringBuilder(count * 48);
            for (var i = start; i < start + count; i++)
            {
                builder.Append(ids[i].ToString("D")).Append(',').Append(MemberTypeCode.ToName(types[i])).Append('\n');
            }

            return builder.ToString();
        }

        public static List<(Guid Id, byte Type)> ParseSegment(string content)
        {
            var entries = new List<(Guid, byte)>(SegmentSize);
            using var reader = new StringReader(content ?? string.Empty);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var comma = line.IndexOf(',');
                if (comma <= 0 ||
                    !Guid.TryParse(line.AsSpan(0, comma), out var id) ||
                    !MemberTypeCode.TryParse(line.Substring(comma + 1).Trim(), out var type) ||
                    type == MemberTypeCode.NotIdentified)
                {
                    throw new InvalidDataException("A selection segment contains an invalid line.");
                }

                entries.Add((id, type));
            }

            return entries;
        }

        public static string FormatReceipt(IEnumerable<(Guid Id, byte Type)> entries)
        {
            var builder = new StringBuilder();
            foreach (var (id, type) in entries)
            {
                builder.Append(JsonSerializer.Serialize(new MemberTypeLine { Id = id, Type = MemberTypeCode.ToName(type) })).Append('\n');
            }

            return builder.ToString();
        }

        /// <summary>Receipt lines: user, agentUser or notIdentified for each unknown identifier of a segment.</summary>
        public static List<(Guid Id, byte Type)> ParseReceipt(string content)
        {
            var entries = new List<(Guid, byte)>();
            using var reader = new StringReader(content ?? string.Empty);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parsed = JsonSerializer.Deserialize<MemberTypeLine>(line);
                if (parsed == null ||
                    parsed.Id == Guid.Empty ||
                    !MemberTypeCode.TryParse(parsed.Type, out var type) ||
                    type == MemberTypeCode.Unknown)
                {
                    throw new InvalidDataException("A selection receipt contains an invalid line.");
                }

                entries.Add((parsed.Id, type));
            }

            return entries;
        }

        /// <summary>
        /// Reads recorded member types line by line into compact arrays. Lines with any other shape, including the
        /// schema line, are ignored. An identifier recorded with two different types is dropped.
        /// </summary>
        public static async Task<CompactTypeMap> ReadRecordedTypesAsync(Stream content, CancellationToken cancellationToken)
        {
            var builder = new CompactTypeMap.Builder();
            using var reader = new StreamReader(content);
            string line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                MemberTypeLine parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<MemberTypeLine>(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (parsed == null ||
                    parsed.Id == Guid.Empty ||
                    !MemberTypeCode.TryParse(parsed.Type, out var type) ||
                    (type != MemberTypeCode.User && type != MemberTypeCode.AgentUser))
                {
                    continue;
                }

                builder.Add(parsed.Id, type);
            }

            return builder.Build(dropConflicts: true);
        }

        public static async Task WriteRecordedTypesAsync(Stream destination, CompactTypeMap types, CancellationToken cancellationToken)
        {
            await using var writer = new StreamWriter(destination, new UTF8Encoding(false), 64 * 1024, leaveOpen: true);
            await writer.WriteAsync(RecordSchemaLine + "\n");
            for (var i = 0; i < types.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteAsync("{\"id\":\"" + types.Ids[i].ToString("D") + "\",\"type\":\"" + MemberTypeCode.ToName(types.Types[i]) + "\"}\n");
            }

            await writer.FlushAsync(cancellationToken);
        }

        /// <summary>Returns the HTTP status of a refused lookup (401 or 403), or null.</summary>
        public static int? RefusedStatus(Exception exception)
        {
            var status = exception switch
            {
                HttpRequestException { StatusCode: not null } http => (int)http.StatusCode.Value,
                ApiException api when api.ResponseStatusCode > 0 => api.ResponseStatusCode,
                _ => 0
            };
            return status == (int)HttpStatusCode.Unauthorized || status == (int)HttpStatusCode.Forbidden
                ? status
                : null;
        }

        /// <summary>
        /// True when a lookup failure is one the shared retry policies retry and the attempts are now exhausted: an
        /// <see cref="HttpRequestException"/> carrying 429 or 500/502/503/504, or one without a status (a transport
        /// failure).
        /// </summary>
        public static bool IsRetriesExhausted(Exception exception) =>
            IsHttpClientTimeout(exception) ||
            exception is HttpRequestException http &&
            (http.StatusCode == null ||
             http.StatusCode == HttpStatusCode.TooManyRequests ||
             http.StatusCode == HttpStatusCode.InternalServerError ||
             http.StatusCode == HttpStatusCode.BadGateway ||
             http.StatusCode == HttpStatusCode.ServiceUnavailable ||
             http.StatusCode == HttpStatusCode.GatewayTimeout);

        /// <summary>
        /// True for a send that ended because the HttpClient timeout elapsed: a <see cref="TaskCanceledException"/>
        /// whose inner exception is a <see cref="TimeoutException"/>. Host or caller cancellation has no such inner
        /// exception.
        /// </summary>
        public static bool IsHttpClientTimeout(Exception exception) =>
            exception is TaskCanceledException { InnerException: TimeoutException };

        public static string StatusText(Exception exception) => exception switch
        {
            TaskCanceledException { InnerException: TimeoutException } => "timeout",
            HttpRequestException { StatusCode: not null } http => ((int)http.StatusCode.Value).ToString(),
            ApiException api when api.ResponseStatusCode > 0 => api.ResponseStatusCode.ToString(),
            _ => "n/a"
        };

        /// <summary>
        /// Reads a selection blob's text, or <see langword="null"/> when it does not exist. Pass the activity's host
        /// cancellation token.
        /// </summary>
        public static async Task<string> ReadTextOrNullAsync(
            IBlobStorageRepository storage,
            string path,
            CancellationToken cancellationToken)
        {
            var blob = await storage.DownloadFileAsync(path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return blob?.BlobStatus == Models.BlobStatus.Found ? blob.Content ?? string.Empty : null;
        }
    }

    /// <summary>
    /// Sorted identifiers with a one-byte type each: about 17 bytes per member.
    /// </summary>
    internal sealed class CompactTypeMap
    {
        public CompactTypeMap(Guid[] ids, byte[] types, int count)
        {
            Ids = ids;
            Types = types;
            Count = count;
        }

        public Guid[] Ids { get; }
        public byte[] Types { get; }
        public int Count { get; }

        public static CompactTypeMap Empty { get; } = new CompactTypeMap(Array.Empty<Guid>(), Array.Empty<byte>(), 0);

        public int IndexOf(Guid id) => Count == 0 ? -1 : Array.BinarySearch(Ids, 0, Count, id);

        public bool TryGet(Guid id, out byte type)
        {
            var index = IndexOf(id);
            type = index >= 0 ? Types[index] : MemberTypeCode.Unknown;
            return index >= 0;
        }

        /// <summary>Appends unsorted pairs into growable compact arrays, then sorts once.</summary>
        internal sealed class Builder
        {
            private Guid[] _ids;
            private byte[] _types;
            private int _count;

            public Builder(int capacity = 1024)
            {
                _ids = new Guid[Math.Max(capacity, 1)];
                _types = new byte[_ids.Length];
            }

            public int Count => _count;

            public void Add(Guid id, byte type)
            {
                if (_count == _ids.Length)
                {
                    var capacity = _ids.Length * 2;
                    Array.Resize(ref _ids, capacity);
                    Array.Resize(ref _types, capacity);
                }

                _ids[_count] = id;
                _types[_count] = type;
                _count++;
            }

            /// <summary>
            /// Sorts and removes duplicate identifiers. With <paramref name="dropConflicts"/>, an identifier listed
            /// with two different types is removed entirely; otherwise the first occurrence is kept.
            /// </summary>
            public CompactTypeMap Build(bool dropConflicts)
            {
                Array.Sort(_ids, _types, 0, _count);
                var write = 0;
                var read = 0;
                while (read < _count)
                {
                    var id = _ids[read];
                    var type = _types[read];
                    var conflict = false;
                    var next = read + 1;
                    while (next < _count && _ids[next] == id)
                    {
                        conflict |= _types[next] != type;
                        next++;
                    }

                    if (!(dropConflicts && conflict))
                    {
                        _ids[write] = id;
                        _types[write] = type;
                        write++;
                    }

                    read = next;
                }

                return new CompactTypeMap(_ids, _types, write);
            }
        }
    }

    internal static class SelectionLog
    {
        public static void TryLog(Action log)
        {
            try
            {
                log();
            }
            catch
            {
                // Observability must never change membership processing.
            }
        }
    }
}
