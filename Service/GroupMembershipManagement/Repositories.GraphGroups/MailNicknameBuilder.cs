// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Text;

namespace Repositories.GraphGroups
{
    /// <summary>
    /// Produces a mail nickname that Microsoft Graph accepts for a Microsoft 365 group.
    /// Keeps printable ASCII except @ ( ) \ [ ] " ; : &lt; &gt; , and
    /// limits the value to 64 characters.
    /// </summary>
    public static class MailNicknameBuilder
    {
        public const int MaxLength = 64;
        private const string EmptyDisplayNameFallbackPrefix = "gmm-group";
        private const int SuffixLength = 8;
        private const string RejectedCharacters = "@()\\[]\";:<>,";

        public static string Build(string suppliedAlias, string displayName)
        {
            var sanitizedAlias = Sanitize(suppliedAlias);

            if (!string.IsNullOrEmpty(sanitizedAlias))
            {
                return sanitizedAlias;
            }

            var suffix = CreateSuffix();
            var sanitizedDisplayName = Sanitize(displayName);
            var prefix = string.IsNullOrEmpty(sanitizedDisplayName) ? EmptyDisplayNameFallbackPrefix : sanitizedDisplayName;

            // Reserve room for the separator and the suffix so the whole value stays within the limit.
            var maxPrefixLength = MaxLength - SuffixLength - 1;
            if (prefix.Length > maxPrefixLength)
            {
                prefix = prefix.Substring(0, maxPrefixLength);
            }

            return Trim($"{prefix}-{suffix}");
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);

            foreach (var character in value)
            {
                if (IsPermitted(character))
                {
                    builder.Append(character);
                }
            }

            var sanitized = builder.ToString();

            if (sanitized.Length > MaxLength)
            {
                sanitized = sanitized.Substring(0, MaxLength);
            }

            return Trim(sanitized);
        }

        private static string Trim(string value)
        {
            return value.Trim('.');
        }

        private static bool IsPermitted(char character)
        {
            return character >= '!' && character <= '~'
                && RejectedCharacters.IndexOf(character) < 0;
        }

        private static string CreateSuffix()
        {
            return Guid.NewGuid().ToString("N").Substring(0, SuffixLength);
        }
    }
}
