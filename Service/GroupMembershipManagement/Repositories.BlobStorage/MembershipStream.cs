// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.ServiceBus;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.BlobStorage
{
    /// <summary>
    /// Reads and writes <see cref="GroupMembership"/> blobs without materializing the complete member collection.
    /// The reader accepts plain JSON or Base64-encoded Brotli JSON; the writer emits the compressed format.
    /// </summary>
    public static class MembershipStream
    {
        // The buffer grows when one JSON token does not fit.
        private const int DefaultBufferSize = 64 * 1024;

        // Reject a single token beyond this point instead of growing until the process runs out of memory.
        private const int MaxBufferSize = 16 * 1024 * 1024;

        // Gather the serializer's small writes before handing them to the compressor.
        private const int CoalesceDrainThreshold = 64 * 1024;
        private const int CoalesceBufferSize = CoalesceDrainThreshold * 2;

        // Match the options used by the existing membership serializers.
        private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        // Exclude members while serializing the envelope.
        private static readonly JsonSerializerOptions _envelopeOnlyOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { RemoveSourceMembersFromContract }
            }
        };

        private static void RemoveSourceMembersFromContract(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Type != typeof(GroupMembership))
            {
                return;
            }

            for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
            {
                if (typeInfo.Properties[i].AttributeProvider is PropertyInfo property
                    && property.Name == nameof(GroupMembership.SourceMembers))
                {
                    typeInfo.Properties.RemoveAt(i);
                }
            }
        }

        // Utf8JsonReader does not accept a byte order mark.
        private static readonly byte[] _utf8Bom = { 0xEF, 0xBB, 0xBF };

        /// <summary>Reads the envelope and yields each member.</summary>
        /// <param name="source">The encoded blob. Left open; the caller keeps ownership.</param>
        /// <param name="onMembershipDetailsKnown">
        /// Called once before the members when the envelope comes first, otherwise after the full read.
        /// SourceMembers is empty.
        /// </param>
        /// <param name="cancellationToken">Cancels the read. Discard yielded members if the read does not complete.</param>
        public static async IAsyncEnumerable<AzureADUser> ReadAsync(
            Stream source,
            Action<GroupMembership> onMembershipDetailsKnown = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var envelope = new GroupMembership { SourceMembers = new List<AzureADUser>() };
            var envelopeReported = false;

            await using var json = await OpenDecodedAsync(source, cancellationToken);

            var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
            var state = new JsonReaderState();
            var bytesInBuffer = 0;
            var isFinalBlock = false;
            var bomStripped = false;

            var context = new ReadContext();

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!isFinalBlock)
                    {
                        var read = await json.ReadAsync(buffer.AsMemory(bytesInBuffer, buffer.Length - bytesInBuffer), cancellationToken);
                        if (read == 0) isFinalBlock = true;
                        else bytesInBuffer += read;
                    }

                    var start = 0;
                    if (!bomStripped)
                    {
                        // A byte order mark may span reads.
                        if (bytesInBuffer >= _utf8Bom.Length)
                        {
                            if (buffer[0] == _utf8Bom[0] && buffer[1] == _utf8Bom[1] && buffer[2] == _utf8Bom[2])
                                start = _utf8Bom.Length;
                            bomStripped = true;
                        }
                        else if (!isFinalBlock)
                        {
                            continue;
                        }
                        else
                        {
                            bomStripped = true;
                        }
                    }

                    var members = new List<AzureADUser>();
                    var consumed = Parse(buffer.AsSpan(start, bytesInBuffer - start), isFinalBlock, ref state, context, envelope, members);
                    consumed += start;

                    if (context.SawEndOfObject && !context.MembersArraySeen)
                    {
                        throw new JsonException(
                            "The membership blob does not contain a SourceMembers array. Use an empty array for an empty membership.");
                    }

                    // Report the envelope before yielding members when the layout allows it.
                    if (context.EnvelopeComplete && !envelopeReported)
                    {
                        envelopeReported = true;
                        onMembershipDetailsKnown?.Invoke(envelope);
                    }

                    foreach (var member in members)
                    {
                        yield return member;
                    }

                    if (consumed < bytesInBuffer)
                    {
                        Buffer.BlockCopy(buffer, consumed, buffer, 0, bytesInBuffer - consumed);
                        bytesInBuffer -= consumed;
                    }
                    else
                    {
                        bytesInBuffer = 0;
                    }

                    if (isFinalBlock) break;

                    if (bytesInBuffer == buffer.Length)
                    {
                        if (buffer.Length >= MaxBufferSize)
                        {
                            throw new JsonException(
                                $"A single JSON token in the membership blob exceeded the {MaxBufferSize} byte limit.");
                        }

                        var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        Buffer.BlockCopy(buffer, 0, larger, 0, bytesInBuffer);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = larger;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (!context.SawEndOfObject)
            {
                throw new JsonException("The membership blob ended before the JSON object was closed.");
            }

        }

        /// <summary>Streams the envelope and members into a compressed membership blob.</summary>
        /// <remarks>
        /// The destination is left open. Discard its contents if this method does not complete.
        /// </remarks>
        public static async Task WriteAsync(
            Stream destination,
            GroupMembership envelope,
            IAsyncEnumerable<AzureADUser> members,
            CancellationToken cancellationToken = default)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (members == null) throw new ArgumentNullException(nameof(members));

            await using var base64 = new CryptoStream(destination, new ToBase64Transform(), CryptoStreamMode.Write, leaveOpen: true);
            await using var brotli = new BrotliStream(base64, CompressionLevel.Fastest, leaveOpen: true);
            await using var coalescer = new CoalescingWriteStream(brotli, CoalesceBufferSize);
            await using var writer = new Utf8JsonWriter(coalescer);

            writer.WriteStartObject();

            WriteEnvelopeWithoutMembers(writer, envelope);

            writer.WritePropertyName("SourceMembers");
            writer.WriteStartArray();

            await foreach (var member in members.WithCancellation(cancellationToken))
            {
                if (member == null)
                    throw new JsonException("A membership stream cannot contain a null member.");

                JsonSerializer.Serialize(writer, member, _serializerOptions);

                // Drain once the serializer fills the coalescing buffer.
                if (coalescer.PendingBytes >= CoalesceDrainThreshold)
                {
                    await coalescer.DrainAsync(cancellationToken);
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken);
            await coalescer.DrainAsync(cancellationToken);
        }

        /// <summary>Opens plain JSON or Base64-encoded Brotli content.</summary>
        private static async Task<Stream> OpenDecodedAsync(Stream source, CancellationToken cancellationToken)
        {
            const int probeLimit = 1024;

            var prefix = new byte[probeLimit];
            var prefixLength = 0;
            var isPlainJson = false;
            var decided = false;

            while (prefixLength < probeLimit)
            {
                var read = await source.ReadAsync(prefix.AsMemory(prefixLength, probeLimit - prefixLength), cancellationToken);
                if (read == 0) break;
                prefixLength += read;

                if (TryClassify(prefix.AsSpan(0, prefixLength), out isPlainJson))
                {
                    decided = true;
                    break;
                }
            }

            if (!decided)
            {
                // Support plain JSON with a long whitespace prefix.
                isPlainJson = true;
            }

            var replayed = new PrefixedStream(prefix, prefixLength, source);

            if (isPlainJson)
            {
                return replayed;
            }

            var base64 = new CryptoStream(replayed, new FromBase64Transform(FromBase64TransformMode.IgnoreWhiteSpaces), CryptoStreamMode.Read, leaveOpen: false);
            return new BrotliStream(base64, CompressionMode.Decompress, leaveOpen: false);
        }

        /// <summary>Classifies the first non-whitespace byte when available.</summary>
        private static bool TryClassify(ReadOnlySpan<byte> prefix, out bool isPlainJson)
        {
            isPlainJson = false;

            var index = 0;
            if (prefix.Length >= _utf8Bom.Length &&
                prefix[0] == _utf8Bom[0] && prefix[1] == _utf8Bom[1] && prefix[2] == _utf8Bom[2])
            {
                index = _utf8Bom.Length;
            }
            else if (prefix.Length < _utf8Bom.Length && prefix.Length > 0 && prefix[0] == _utf8Bom[0])
            {
                // The byte order mark may be incomplete.
                return false;
            }

            while (index < prefix.Length &&
                   (prefix[index] == (byte)' ' || prefix[index] == (byte)'\t' ||
                    prefix[index] == (byte)'\r' || prefix[index] == (byte)'\n'))
            {
                index++;
            }

            if (index >= prefix.Length) return false;

            // '{' is not part of the Base64 alphabet.
            isPlainJson = prefix[index] == (byte)'{';
            return true;
        }

        /// <summary>Writes every envelope property except SourceMembers.</summary>
        private static void WriteEnvelopeWithoutMembers(Utf8JsonWriter writer, GroupMembership envelope)
        {
            var envelopeJson = JsonSerializer.SerializeToUtf8Bytes(envelope, _envelopeOnlyOptions);

            using var document = JsonDocument.Parse(envelopeJson);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }
        }

        /// <summary>Parses one buffered chunk and appends completed members.</summary>
        private static int Parse(
            ReadOnlySpan<byte> span,
            bool isFinalBlock,
            ref JsonReaderState state,
            ReadContext context,
            GroupMembership envelope,
            List<AzureADUser> members)
        {
            var reader = new Utf8JsonReader(span, isFinalBlock, state);

            while (true)
            {
                // Checkpoint tokens that may span buffered chunks.
                var beforeToken = reader;
                if (!reader.Read()) break;

                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        context.CurrentProperty = reader.GetString();
                        // Keep the root property until its value is handled.
                        if (!context.InsideMembers && context.ObjectDepth == 1)
                            context.RootProperty = context.CurrentProperty;
                        break;

                    case JsonTokenType.StartArray:
                        RejectStructuredValueForScalar(context, JsonTokenType.StartArray);
                        if (context.ObjectDepth == 1 && IsObjectEnvelopeProperty(context.RootProperty))
                            throw Malformed(context.RootProperty, JsonTokenType.StartArray);

                        // Nested arrays are not valid members.
                        if (context.InsideMembers)
                            throw Malformed("SourceMembers", JsonTokenType.StartArray);
                        // Only the root SourceMembers property contains members.
                        if (string.Equals(context.RootProperty, "SourceMembers", StringComparison.Ordinal))
                        {
                            // Yielded members cannot be replaced by a repeated array.
                            if (context.MembersArraySeen)
                                throw Malformed("SourceMembers", JsonTokenType.StartArray);

                            context.InsideMembers = true;
                            context.MembersArraySeen = true;
                        }
                        context.RootProperty = null;
                        break;

                    case JsonTokenType.EndArray:
                        if (context.InsideMembers)
                            context.InsideMembers = false;
                        break;

                    case JsonTokenType.StartObject:
                        // Rewind incomplete objects so they can be retried with more data.
                        if (context.InsideMembers)
                        {
                            var atMember = reader;
                            if (!reader.TrySkip())
                            {
                                reader = beforeToken;
                                state = reader.CurrentState;
                                return (int)reader.BytesConsumed;
                            }

                            reader = atMember;
                            members.Add(JsonSerializer.Deserialize<AzureADUser>(ref reader, _serializerOptions));
                            context.RootProperty = null;
                            break;
                        }

                        if (string.Equals(context.RootProperty, "SourceMembers", StringComparison.Ordinal))
                            throw Malformed("SourceMembers", JsonTokenType.StartObject);

                        // Deserialize object-valued fields whole so all model properties survive.
                        if (IsObjectEnvelopeProperty(context.RootProperty))
                        {
                            var objectProperty = context.RootProperty;

                            RejectPropertyAfterMembers(context, objectProperty);

                            var atValue = reader;
                            if (!reader.TrySkip())
                            {
                                reader = beforeToken;
                                state = reader.CurrentState;
                                return (int)reader.BytesConsumed;
                            }

                            reader = atValue;
                            if (string.Equals(objectProperty, "Destination", StringComparison.Ordinal))
                                envelope.Destination = JsonSerializer.Deserialize<AzureADGroup>(ref reader, _serializerOptions);
                            else
                                envelope.SyncJob = JsonSerializer.Deserialize<SyncJob>(ref reader, _serializerOptions);

                            context.RootProperty = null;
                            break;
                        }

                        RejectStructuredValueForScalar(context, JsonTokenType.StartObject);
                        context.ObjectDepth++;
                        context.RootProperty = null;
                        break;

                    case JsonTokenType.EndObject:
                        context.ObjectDepth--;
                        if (context.ObjectDepth == 0)
                        {
                            context.SawEndOfObject = true;
                            context.EnvelopeComplete = true;
                        }
                        break;

                    default:
                        // Only root values may update the envelope.
                        if (context.InsideMembers)
                        {
                            // Only objects are valid members.
                            if (reader.TokenType != JsonTokenType.Comment)
                                throw Malformed("SourceMembers", reader.TokenType);
                        }
                        else if (context.ObjectDepth == 1)
                        {
                            ReadEnvelopeValue(ref reader, context, envelope);
                        }
                        context.RootProperty = null;
                        break;
                }

                // Report early only when envelope scalars preceded the members.
                if (context.InsideMembers && !context.EnvelopeComplete && context.SawEnvelopeScalarBeforeMembers)
                {
                    context.EnvelopeComplete = true;
                }
            }

            state = reader.CurrentState;
            return (int)reader.BytesConsumed;
        }

        private static Guid ReadGuid(ref Utf8JsonReader reader, string propertyName)
        {
            if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out var value))
                throw Malformed(propertyName, reader.TokenType);
            return value;
        }

        private static bool ReadBoolean(ref Utf8JsonReader reader, string propertyName)
        {
            if (reader.TokenType != JsonTokenType.True && reader.TokenType != JsonTokenType.False)
                throw Malformed(propertyName, reader.TokenType);
            return reader.GetBoolean();
        }

        private static int ReadInt32(ref Utf8JsonReader reader, string propertyName)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var value))
                throw Malformed(propertyName, reader.TokenType);
            return value;
        }

        private static int? ReadNullableInt32(ref Utf8JsonReader reader, string propertyName)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;
            return ReadInt32(ref reader, propertyName);
        }

        private static JsonException Malformed(string propertyName, JsonTokenType tokenType) =>
            new JsonException($"'{propertyName}' is present with a {tokenType} value that cannot be read. Only an omitted property may fall back to its default.");

        /// <summary>Returns whether the reader binds the property as one scalar value.</summary>
        private static bool IsScalarEnvelopeProperty(string propertyName) =>
            string.Equals(propertyName, "RunId", StringComparison.Ordinal) ||
            string.Equals(propertyName, "SyncJobId", StringComparison.Ordinal) ||
            string.Equals(propertyName, "Exclusionary", StringComparison.Ordinal) ||
            string.Equals(propertyName, "MembershipObtainerDryRunEnabled", StringComparison.Ordinal) ||
            string.Equals(propertyName, "IsLastMessage", StringComparison.Ordinal) ||
            string.Equals(propertyName, "MessageIndex", StringComparison.Ordinal) ||
            string.Equals(propertyName, "TotalMessageCount", StringComparison.Ordinal) ||
            string.Equals(propertyName, "ProjectedMemberCount", StringComparison.Ordinal) ||
            string.Equals(propertyName, "TotalMembersToAdd", StringComparison.Ordinal) ||
            string.Equals(propertyName, "TotalMembersToRemove", StringComparison.Ordinal) ||
            string.Equals(propertyName, "Query", StringComparison.Ordinal);

        /// <summary>Returns whether the reader deserializes the property as an object.</summary>
        private static bool IsObjectEnvelopeProperty(string propertyName) =>
            string.Equals(propertyName, "Destination", StringComparison.Ordinal) ||
            string.Equals(propertyName, "SyncJob", StringComparison.Ordinal);

        private static void RejectStructuredValueForScalar(ReadContext context, JsonTokenType tokenType)
        {
            // Object fields and members are validated by their deserializers.
            if (context.ObjectDepth == 1 && IsScalarEnvelopeProperty(context.CurrentProperty))
                throw Malformed(context.CurrentProperty, tokenType);
        }

        /// <summary>Rejects envelope fields that arrive after early reporting.</summary>
        private static void RejectPropertyAfterMembers(ReadContext context, string propertyName)
        {
            if (!context.MembersArraySeen || !context.SawEnvelopeScalarBeforeMembers)
                return;

            throw new JsonException(
                $"'{propertyName}' appears after the members, in a stream whose classification precedes " +
                "them. Such a stream describes itself before its own values are final, and the description cannot " +
                "be corrected once the caller has it.");
        }

        private static void ReadEnvelopeValue(ref Utf8JsonReader reader, ReadContext context, GroupMembership envelope)
        {
            if (IsObjectEnvelopeProperty(context.CurrentProperty))
            {
                RejectPropertyAfterMembers(context, context.CurrentProperty);

                if (reader.TokenType != JsonTokenType.Null)
                    throw Malformed(context.CurrentProperty, reader.TokenType);

                if (string.Equals(context.CurrentProperty, "Destination", StringComparison.Ordinal))
                    envelope.Destination = null;
                else
                    envelope.SyncJob = null;

                return;
            }

            if (string.Equals(context.CurrentProperty, "SourceMembers", StringComparison.Ordinal))
            {
                if (reader.TokenType != JsonTokenType.Null)
                    throw Malformed("SourceMembers", reader.TokenType);

                if (context.MembersArraySeen)
                    throw new JsonException(
                        "'SourceMembers' appears again after its members were already streamed.");

                return;
            }

            if (IsScalarEnvelopeProperty(context.CurrentProperty))
                RejectPropertyAfterMembers(context, context.CurrentProperty);

            if (!context.MembersArraySeen && IsScalarEnvelopeProperty(context.CurrentProperty))
                context.SawEnvelopeScalarBeforeMembers = true;

            switch (context.CurrentProperty)
            {
                case "RunId":
                    envelope.RunId = ReadGuid(ref reader, "RunId");
                    break;

                case "SyncJobId":
                    envelope.SyncJobId = ReadGuid(ref reader, "SyncJobId");
                    break;

                case "Exclusionary":
                    envelope.Exclusionary = ReadBoolean(ref reader, "Exclusionary");
                    break;

                case "MembershipObtainerDryRunEnabled":
                    envelope.MembershipObtainerDryRunEnabled = ReadBoolean(ref reader, "MembershipObtainerDryRunEnabled");
                    break;

                case "IsLastMessage":
                    envelope.IsLastMessage = ReadBoolean(ref reader, "IsLastMessage");
                    break;

                case "MessageIndex":
                    envelope.MessageIndex = ReadInt32(ref reader, "MessageIndex");
                    break;

                case "TotalMessageCount":
                    envelope.TotalMessageCount = ReadInt32(ref reader, "TotalMessageCount");
                    break;

                case "ProjectedMemberCount":
                    envelope.ProjectedMemberCount = ReadNullableInt32(ref reader, "ProjectedMemberCount");
                    break;

                case "TotalMembersToAdd":
                    envelope.TotalMembersToAdd = ReadNullableInt32(ref reader, "TotalMembersToAdd");
                    break;

                case "TotalMembersToRemove":
                    envelope.TotalMembersToRemove = ReadNullableInt32(ref reader, "TotalMembersToRemove");
                    break;

                case "Query":
                    if (reader.TokenType == JsonTokenType.Null) { envelope.Query = null; break; }
                    if (reader.TokenType != JsonTokenType.String) throw Malformed("Query", reader.TokenType);
                    envelope.Query = reader.GetString();
                    break;
            }
        }

        private sealed class ReadContext
        {
            public string CurrentProperty { get; set; }
            public string RootProperty { get; set; }
            public bool InsideMembers { get; set; }
            public bool MembersArraySeen { get; set; }
            public bool SawEnvelopeScalarBeforeMembers { get; set; }
            public int ObjectDepth { get; set; }
            public bool EnvelopeComplete { get; set; }
            public bool SawEndOfObject { get; set; }
        }
    }
}
