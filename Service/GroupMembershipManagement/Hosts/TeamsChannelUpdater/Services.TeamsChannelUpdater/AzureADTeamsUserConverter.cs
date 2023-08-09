// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Entities;
using Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services.TeamsChannelUpdater.Contracts
{
    public class AzureADTeamsUserConverter : JsonConverter<AzureADTeamsUser>
    {
        public override AzureADTeamsUser Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {

            // Create a new instance of MyCustomType
            var azureADTeamsUser = new AzureADTeamsUser();

            while (reader.Read())
            {
                // Break if we have reached the end of the object
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected property name");

                // Read the property name
                string propertyName = reader.GetString();

                // Read the property value and assign it to the appropriate property of MyCustomType
                reader.Read();
                switch (propertyName)
                {
                    case "ObjectId":
                        azureADTeamsUser.ObjectId = JsonSerializer.Deserialize<Guid>(ref reader, options);
                        break;
                    case "MembershipAction":
                        azureADTeamsUser.MembershipAction = JsonSerializer.Deserialize<MembershipAction>(ref reader, options);
                        break;
                    case "Properties":
                        azureADTeamsUser.Properties = JsonSerializer.Deserialize<TeamsUserProperties>(ref reader, options);
                        break;
                    // Handle other properties as needed
                    default:
                        reader.Skip(); // Skip unknown properties
                        break;
                }
            }

            return azureADTeamsUser;
        }

        public override void Write(Utf8JsonWriter writer, AzureADTeamsUser value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WriteString("ObjectId", value.ObjectId);
            writer.WriteNumber("MembershipAction", (int)value.MembershipAction);

            if (value.MembershipAction == MembershipAction.Remove)
            {
                writer.WritePropertyName("Properties");
                writer.WriteStartObject();
                writer.WriteString("ConversationMemberId", value.ConversationMemberId);
                writer.WriteEndObject();
            }

            // Write additional properties as needed
            writer.WriteEndObject();
        }
    }
}
