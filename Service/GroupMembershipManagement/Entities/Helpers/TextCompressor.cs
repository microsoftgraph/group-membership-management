// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Entities.Helpers
{
    public static class TextCompressor
    {
        /// <summary>
        /// Compresses a string
        /// </summary>
        /// <param name="input">A string</param>
        /// <returns>Base64 compressed string</returns>
        public static string Compress(string input)
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

        /// <summary>
        /// Decompresses Base64 string
        /// </summary>
        /// <param name="input">A Base64 string</param>
        /// <returns>string</returns>
        public static string Decompress(string input)
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
    }
}
