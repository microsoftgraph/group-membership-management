// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Text.Json;

namespace Services.Notifier
{
    public static class NotificationMessageContentParser
    {
        public static JsonElement ParseMessageBody(string messageBody)
        {
            if (string.IsNullOrWhiteSpace(messageBody))
            {
                throw new JsonException("Notification message body cannot be null or empty.");
            }

            var messageContent = JsonDocument.Parse(messageBody).RootElement.Clone();
            if (messageContent.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Notification message body must be a JSON object.");
            }

            return messageContent;
        }

        public static T GetRequiredValue<T>(JsonElement messageContent, string propertyName)
        {
            if (!messageContent.TryGetProperty(propertyName, out var propertyValue))
            {
                throw new JsonException($"Notification message body is missing required '{propertyName}' property.");
            }

            return DeserializeRequired<T>(propertyValue, propertyName);
        }

        public static string GetRequiredString(JsonElement messageContent, string propertyName)
        {
            if (!messageContent.TryGetProperty(propertyName, out var propertyValue))
            {
                throw new JsonException($"Notification message body is missing required '{propertyName}' property.");
            }

            if (propertyValue.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"Notification message body property '{propertyName}' must be a string.");
            }

            var value = propertyValue.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new JsonException($"Notification message body property '{propertyName}' cannot be null or empty.");
            }

            return value;
        }

        public static string[] GetOptionalStringArray(JsonElement messageContent, string propertyName)
        {
            if (!messageContent.TryGetProperty(propertyName, out var propertyValue)
                || propertyValue.ValueKind == JsonValueKind.Null
                || propertyValue.ValueKind == JsonValueKind.Undefined)
            {
                return Array.Empty<string>();
            }

            if (propertyValue.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException($"Notification message body property '{propertyName}' must be an array.");
            }

            var values = propertyValue.Deserialize<string[]>();
            return values ?? Array.Empty<string>();
        }

        private static T DeserializeRequired<T>(JsonElement propertyValue, string propertyName)
        {
            if (propertyValue.ValueKind == JsonValueKind.Null || propertyValue.ValueKind == JsonValueKind.Undefined)
            {
                throw new JsonException($"Notification message body property '{propertyName}' cannot be null.");
            }

            var value = propertyValue.Deserialize<T>();
            if (value == null)
            {
                throw new JsonException($"Notification message body property '{propertyName}' could not be deserialized to {typeof(T).Name}.");
            }

            return value;
        }
    }
}
