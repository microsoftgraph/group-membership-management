// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Models.Helpers
{
    public class DestinationValueConverter : JsonConverter<DestinationValue>
    {
        public override DestinationValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using (var doc = JsonDocument.ParseValue(ref reader))
            {
                // Check for the unique property that identifies the subclass
                if (doc.RootElement.TryGetProperty("ChannelId", out var _))
                {
                    // Deserialize as TeamsChannelDestinationValue
                    return JsonSerializer.Deserialize<TeamsChannelDestinationValue>(doc.RootElement.GetRawText(), options);
                }
                else
                {
                    // Assume it's a GroupDestinationValue if it doesn't match other types
                    return JsonSerializer.Deserialize<GroupDestinationValue>(doc.RootElement.GetRawText(), options);
                }
            }
        }

        public override void Write(Utf8JsonWriter writer, DestinationValue value, JsonSerializerOptions options)
        {
            // Serialize based on the runtime type of the value
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }

}
