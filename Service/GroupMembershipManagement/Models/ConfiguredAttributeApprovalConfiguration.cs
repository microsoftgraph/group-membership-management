// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Models
{
    public class ConfiguredAttributeApprovalRow
    {
        public string Attribute { get; set; } = string.Empty;
        public List<string> Values { get; set; } = new();
    }

    public class ConfiguredAttributeApprovalConfiguration
    {
        public const int MaxAttributes = 32;
        public const int MaxValuesPerAttribute = 100;
        public const int MaxValueLength = 256;
        public const int MaxDocumentBytes = 65536;
        public const int MinDepth = 1;
        public const int MaxDepthLimit = 5;
        public const int DefaultDepth = 2;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        public bool Enabled { get; set; }
        public int SchemaVersion { get; set; } = 1;
        public int MaxDepth { get; set; } = DefaultDepth;
        public List<ConfiguredAttributeApprovalRow> Attributes { get; set; } = new();

        public void Validate()
        {
            if (SchemaVersion != 1)
                throw new ArgumentException("The configuration schema version is not supported.");
            if (MaxDepth < MinDepth || MaxDepth > MaxDepthLimit)
                throw new ArgumentException($"The allowed levels below the manager must be between {MinDepth} and {MaxDepthLimit}.");
            if (Attributes == null || Attributes.Count > MaxAttributes || (Enabled && Attributes.Count == 0))
                throw new ArgumentException($"Configure between one and {MaxAttributes} fields before enabling this rule.");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Attributes)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Attribute)
                    || row.Attribute.Length > 128
                    || !Regex.IsMatch(row.Attribute, @"\A[A-Za-z_][A-Za-z0-9_-]*\z")
                    || !names.Add(row.Attribute))
                    throw new ArgumentException("Each field must have a unique valid attribute.");
                if (row.Values == null || row.Values.Count == 0 || row.Values.Count > MaxValuesPerAttribute
                    || row.Values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > MaxValueLength)
                    || row.Values.Distinct(StringComparer.Ordinal).Count() != row.Values.Count)
                    throw new ArgumentException($"Each field must have between one and {MaxValuesPerAttribute} unique, nonblank values.");
            }
        }

        // Values supplied here must already be canonicalized according to trusted source types.
        public bool Matches(IReadOnlyDictionary<string, string> requestorValues)
        {
            Validate();
            return Enabled && Attributes.Count > 0 && requestorValues != null
                && Attributes.All(row => requestorValues.TryGetValue(row.Attribute, out var value)
                    && !string.IsNullOrWhiteSpace(value) && row.Values.Contains(value, StringComparer.Ordinal));
        }

        public string SerializeDocument()
        {
            Validate();
            var json = JsonSerializer.Serialize(new StoredDocument
            {
                SchemaVersion = SchemaVersion,
                MaxDepth = MaxDepth,
                Attributes = Attributes
            }, JsonOptions);
            if (Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
                throw new ArgumentException($"The configuration exceeds {MaxDocumentBytes} bytes.");
            return json;
        }

        public static ConfiguredAttributeApprovalConfiguration FromSettings(IEnumerable<Setting> settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var rows = settings.Where(setting => IsConfigurationKey(setting.SettingKey)).ToList();
            if (rows.Count == 0)
                return new ConfiguredAttributeApprovalConfiguration();
            if (rows.Count != 2
                || rows.Count(row => row.SettingKey == SettingKey.IsAutoApprovalForConfiguredAttributeEnabled) != 1
                || rows.Count(row => row.SettingKey == SettingKey.ConfiguredAttributeAutoApprovalConfiguration) != 1)
                throw new InvalidOperationException("The configured-attribute settings are incomplete.");

            var flag = rows.Single(row => row.SettingKey == SettingKey.IsAutoApprovalForConfiguredAttributeEnabled);
            var document = rows.Single(row => row.SettingKey == SettingKey.ConfiguredAttributeAutoApprovalConfiguration);
            if (!bool.TryParse(flag.SettingValue, out var enabled)
                || string.IsNullOrWhiteSpace(document.SettingValue)
                || Encoding.UTF8.GetByteCount(document.SettingValue) > MaxDocumentBytes)
                throw new InvalidOperationException("The configured-attribute settings are invalid.");

            try
            {
                var stored = JsonSerializer.Deserialize<StoredDocument>(document.SettingValue, JsonOptions)
                    ?? throw new ArgumentException("A configuration document is required.");
                var result = new ConfiguredAttributeApprovalConfiguration
                {
                    Enabled = enabled,
                    SchemaVersion = stored.SchemaVersion,
                    MaxDepth = stored.MaxDepth ?? DefaultDepth,
                    Attributes = stored.Attributes
                };
                result.Validate();
                return result;
            }
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException)
            {
                throw new InvalidOperationException("The configured-attribute document is invalid.", exception);
            }
        }

        public static bool IsConfigurationKey(SettingKey key) =>
            key == SettingKey.IsAutoApprovalForConfiguredAttributeEnabled
            || key == SettingKey.ConfiguredAttributeAutoApprovalConfiguration;

        private sealed class StoredDocument
        {
            public int SchemaVersion { get; set; }
            public int? MaxDepth { get; set; }
            public List<ConfiguredAttributeApprovalRow> Attributes { get; set; }
        }
    }
}
