// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models.Helpers;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Models.Tests
{
    [TestClass]
    public sealed class TextCompressorTests
    {
        private static readonly string[] _corpus = BuildCorpus().ToArray();

        [TestMethod]
        public void Decompress_ContentWrittenByThePreviousImplementation_RoundTrips()
        {
            foreach (var original in _corpus)
            {
                var legacyBlob = LegacyCompress(original);

                Assert.AreEqual(original, TextCompressor.Decompress(legacyBlob), Describe(original));
            }
        }

        [TestMethod]
        public void PreviousImplementation_CanDecompressContentWrittenNow()
        {
            foreach (var original in _corpus)
            {
                var blob = TextCompressor.Compress(original);

                Assert.AreEqual(original, LegacyDecompress(blob), Describe(original));
            }
        }

        [TestMethod]
        public void CompressThenDecompress_ReturnsTheOriginal()
        {
            foreach (var original in _corpus)
            {
                Assert.AreEqual(original, TextCompressor.Decompress(TextCompressor.Compress(original)), Describe(original));
            }
        }

        [TestMethod]
        public void SurrogatePairs_SurviveAtAnyOffset()
        {
            foreach (var leadingLength in new[] { 0, 1, 4095, 65535, 65536, 131071 })
            {
                var original = new string('a', leadingLength) + "\U0001F600" + new string('b', 32);

                var restored = TextCompressor.Decompress(TextCompressor.Compress(original));

                Assert.AreEqual(original, restored, $"surrogate pair starting at index {leadingLength}");
                Assert.IsFalse(restored.Contains('\uFFFD'), $"replacement character at index {leadingLength}");
            }
        }

        [TestMethod]
        public void UnpairedSurrogates_AreReplacedIdenticallyByBothImplementations()
        {
            foreach (var original in new[] { "lone high surrogate \uD83D end", "lone low surrogate \uDE00 end" })
            {
                var viaLegacy = LegacyDecompress(LegacyCompress(original));

                Assert.AreEqual(viaLegacy, TextCompressor.Decompress(TextCompressor.Compress(original)), original);
                Assert.AreEqual(viaLegacy, TextCompressor.Decompress(LegacyCompress(original)), original);
                Assert.AreEqual(viaLegacy, LegacyDecompress(TextCompressor.Compress(original)), original);

                Assert.AreNotEqual(original, viaLegacy, "an unpaired surrogate is expected to be lossy");
                Assert.IsTrue(viaLegacy.Contains('\uFFFD'), "expected the replacement character");
            }
        }

        [TestMethod]
        public void ContentOfVariousSizes_RoundTrips()
        {
            foreach (var length in new[] { 0, 1, 4095, 4096, 65535, 65536, 65537, 200000 })
            {
                var original = BuildRepeatingText(length);

                Assert.AreEqual(original, TextCompressor.Decompress(TextCompressor.Compress(original)), $"length {length}");
                Assert.AreEqual(original, LegacyDecompress(TextCompressor.Compress(original)), $"length {length} read by the previous implementation");
            }
        }

        [TestMethod]
        public void Compress_Null_ReturnsNull()
        {
            Assert.IsNull(TextCompressor.Compress(null));
        }

        [TestMethod]
        public void Decompress_Null_ReturnsNull()
        {
            Assert.IsNull(TextCompressor.Decompress(null));
        }

        [TestMethod]
        public void Compress_EmptyString_RoundTripsInBothImplementations()
        {
            var blob = TextCompressor.Compress(string.Empty);

            Assert.AreEqual(string.Empty, TextCompressor.Decompress(blob));
            Assert.AreEqual(string.Empty, LegacyDecompress(blob));
            Assert.AreEqual(string.Empty, TextCompressor.Decompress(LegacyCompress(string.Empty)));
        }

        [TestMethod]
        public void Compress_ProducesAFinalizedBrotliStream()
        {
            foreach (var original in new[] { string.Empty, "a", BuildMembershipJson(500), BuildRepeatingText(70000) })
            {
                var expected = Encoding.Default.GetBytes(original);
                var compressed = Convert.FromBase64String(TextCompressor.Compress(original));
                var actual = new byte[Math.Max(1, expected.Length)];

                var decoder = new BrotliDecoder();
                var status = decoder.Decompress(compressed, actual, out var bytesConsumed, out var bytesWritten);

                Assert.AreEqual(OperationStatus.Done, status, Describe(original));
                Assert.AreEqual(compressed.Length, bytesConsumed, Describe(original));
                Assert.AreEqual(expected.Length, bytesWritten, Describe(original));
                CollectionAssert.AreEqual(expected, actual.Take(bytesWritten).ToArray(), Describe(original));
            }
        }

        [TestMethod]
        public void HighEntropyMembership_RoundTripsInBothDirections()
        {
            var original = BuildMembershipJson(50000);

            Assert.AreEqual(original, TextCompressor.Decompress(TextCompressor.Compress(original)));
            Assert.AreEqual(original, LegacyDecompress(TextCompressor.Compress(original)));
            Assert.AreEqual(original, TextCompressor.Decompress(LegacyCompress(original)));
        }

        [TestMethod]
        public void Compress_OutputStaysWithinAFewCharactersOfThePreviousImplementation()
        {
            const int AllowedDrift = 4;

            foreach (var original in _corpus)
            {
                var current = TextCompressor.Compress(original).Length;
                var legacy = LegacyCompress(original).Length;

                Assert.IsTrue(
                    Math.Abs(current - legacy) <= AllowedDrift,
                    $"{Describe(original)}: current {current} chars vs previous {legacy} chars");
            }
        }

        private static string Describe(string value) =>
            value == null ? "null" : $"entry of length {value.Length}";

        private static IEnumerable<string> BuildCorpus()
        {
            yield return string.Empty;
            yield return "a";
            yield return "{}";
            yield return "{\"SourceMembers\":[],\"Exclusionary\":false}";
            yield return BuildMembershipJson(1);
            yield return BuildMembershipJson(500);
            yield return BuildMembershipJson(5000);
            yield return BuildRepeatingText(70000);
            yield return new string('x', 140000);

            // Multi-byte and astral-plane characters. Unpaired surrogates are deliberately absent
            // here because they cannot survive UTF-8 at all; they are covered separately.
            yield return "\u4F60\u597D\u4E16\u754C";
            yield return "emoji \U0001F600\U0001F601\U0001F602 mixed with ascii";
            yield return string.Concat(Enumerable.Repeat("\U0001F600", 40000));
        }

        private static string BuildRepeatingText(int length)
        {
            const string Pattern = "The quick brown fox jumps over the lazy dog. 0123456789. ";

            if (length == 0)
                return string.Empty;

            var builder = new StringBuilder(length);
            while (builder.Length < length)
            {
                builder.Append(Pattern, 0, Math.Min(Pattern.Length, length - builder.Length));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Members carry high-entropy identifiers so the generated content compresses at a ratio
        /// close to production. Sequential GUIDs are almost entirely zero bytes and compress about
        /// four times better than real data, which would make any size or memory measurement taken
        /// against this corpus misleading. The seed is fixed so failures reproduce.
        /// </summary>
        private static string BuildMembershipJson(int memberCount)
        {
            var random = new Random(20260825);
            var objectId = new byte[16];
            var builder = new StringBuilder();
            builder.Append("{\"Exclusionary\":false,\"SyncJobId\":\"1a2b3c4d-0000-0000-0000-000000000001\",\"SourceMembers\":[");

            for (var i = 0; i < memberCount; i++)
            {
                if (i > 0)
                    builder.Append(',');

                random.NextBytes(objectId);

                builder.Append("{\"ObjectId\":\"");
                builder.Append(new Guid(objectId).ToString());
                builder.Append("\",\"MembershipAction\":null}");
            }

            builder.Append("]}");
            return builder.ToString();
        }

        /// <summary>
        /// These two must stay a byte-faithful copy of the implementation that shipped before this
        /// change. They are the only thing proving that content already in storage still decodes,
        /// and that a host still running the previous build can read what this one writes. Updating
        /// them to match the current implementation would silently void both guarantees. The
        /// nullable context is disabled to match the project the original is compiled in, so the
        /// copy can stay literal rather than being adjusted to satisfy a different setting.
        /// </summary>
#nullable disable
        private static string LegacyCompress(string input)
        {
            if (input == null)
                return input;

            var inputBytes = Encoding.Default.GetBytes(input);
            using (var sourceMS = new MemoryStream(inputBytes))
            using (var destinationMS = new MemoryStream())
            using (var brotli = new BrotliStream(destinationMS, CompressionLevel.Fastest))
            {
                sourceMS.CopyTo(brotli);
                brotli.Flush();
                var outputBytes = destinationMS.ToArray();
                return Convert.ToBase64String(outputBytes);
            }
        }

        private static string LegacyDecompress(string input)
        {
            if (input == null)
                return input;

            var inputBytes = Convert.FromBase64String(input);
            using (var inputStream = new MemoryStream(inputBytes))
            using (var outputStream = new MemoryStream())
            using (var decompressStream = new BrotliStream(inputStream, CompressionMode.Decompress))
            {
                inputStream.Flush();
                decompressStream.Flush();
                decompressStream.CopyTo(outputStream);
                var outputBytes = outputStream.ToArray();
                return Encoding.Default.GetString(outputBytes);
            }
        }
#nullable restore
    }
}
