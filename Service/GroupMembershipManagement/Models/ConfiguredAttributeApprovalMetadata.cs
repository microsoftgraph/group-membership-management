// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Models
{
    public static class ConfiguredAttributeApprovalMetadata
    {
        private const string CodeSuffix = "_Code";

        private static SqlMembershipAttribute FromColumn(string columnName, string type)
        {
            var hasMapping = columnName.EndsWith(CodeSuffix, StringComparison.Ordinal);
            return new SqlMembershipAttribute
            {
                Name = hasMapping ? columnName.Substring(0, columnName.Length - CodeSuffix.Length) : columnName,
                Type = type,
                CustomLabel = "",
                Description = "",
                Enabled = true,
                HasMapping = hasMapping
            };
        }

        private static SqlMembershipAttribute ApplySettings(SqlMembershipAttribute attribute, SqlMembershipAttribute stored)
        {
            attribute.CustomLabel = stored?.CustomLabel ?? "";
            attribute.Description = stored?.Description ?? "";
            attribute.Enabled = stored?.Enabled ?? true;
            attribute.IsSensitive = stored?.IsSensitive ?? false;
            attribute.NullThreshold = stored?.NullThreshold;
            return attribute;
        }

        public static List<SqlMembershipAttribute> Resolve(
            IEnumerable<(string Name, string Type)> columns, IEnumerable<SqlMembershipAttribute> settings)
        {
            if (columns == null || settings == null)
                throw new InvalidOperationException("Attribute metadata is unavailable.");
            var configured = new Dictionary<string, SqlMembershipAttribute>(StringComparer.OrdinalIgnoreCase);
            foreach (var setting in settings)
            {
                if (setting == null || string.IsNullOrWhiteSpace(setting.Name) || !configured.TryAdd(setting.Name, setting))
                    throw new InvalidOperationException("Attribute settings have invalid or ambiguous identities.");
            }
            var result = new List<SqlMembershipAttribute>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                if (string.IsNullOrWhiteSpace(column.Name) || string.IsNullOrWhiteSpace(column.Type))
                    throw new InvalidOperationException("Attribute column metadata is invalid.");
                var mapped = column.Name.EndsWith("_Code", StringComparison.Ordinal);
                var name = mapped ? column.Name.Substring(0, column.Name.Length - 5) : column.Name;
                if (!names.Add(name))
                    throw new InvalidOperationException("Attribute metadata has ambiguous column identities.");
                configured.TryGetValue(name, out var setting);
                result.Add(ApplySettings(FromColumn(column.Name, column.Type), setting));
            }
            if (result.Count == 0)
                throw new InvalidOperationException("Attribute metadata is empty.");
            return result;
        }

        public static bool IsEligible(SqlMembershipAttribute attribute) =>
            attribute != null && attribute.Enabled && IsSupportedType(attribute.Type);

        public static string GetColumnName(SqlMembershipAttribute attribute) =>
            attribute.HasMapping ? attribute.Name + "_Code" : attribute.Name;

        public static bool IsMissingValue(string value) =>
            string.IsNullOrWhiteSpace(value) || value == "-1"
            || string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "UNKNOWN", StringComparison.OrdinalIgnoreCase);

        public static bool TryCanonicalize(string value, string type, out string canonical)
        {
            canonical = string.Empty;
            if (IsMissingValue(value) || value.Length > ConfiguredAttributeApprovalConfiguration.MaxValueLength)
                return false;
            var sourceType = type?.ToLowerInvariant();
            switch (sourceType)
            {
                case "char":
                case "nchar":
                case "varchar":
                case "nvarchar":
                    canonical = value;
                    return true;
                case "tinyint":
                case "smallint":
                case "int":
                case "bigint":
                    if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                        return false;
                    if ((sourceType == "tinyint" && (integer < 0 || integer > byte.MaxValue))
                        || (sourceType == "smallint" && (integer < short.MinValue || integer > short.MaxValue))
                        || (sourceType == "int" && (integer < int.MinValue || integer > int.MaxValue)))
                        return false;
                    canonical = integer.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "numeric":
                case "decimal":
                case "money":
                case "smallmoney":
                    if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var number))
                        return false;
                    canonical = number.ToString("G29", CultureInfo.InvariantCulture);
                    return true;
                case "bit":
                    if (value != "0" && value != "1")
                        return false;
                    canonical = value;
                    return true;
                case "uniqueidentifier":
                    if (!Guid.TryParse(value, out var guid) || guid == Guid.Empty)
                        return false;
                    canonical = guid.ToString("D");
                    return true;
                case "date":
                    if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                        return false;
                    canonical = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsSupportedType(string type) => type?.ToLowerInvariant() is
            "char" or "nchar" or "varchar" or "nvarchar" or "tinyint" or "smallint" or "int" or "bigint"
            or "numeric" or "decimal" or "money" or "smallmoney" or "bit" or "uniqueidentifier" or "date";
    }
}
