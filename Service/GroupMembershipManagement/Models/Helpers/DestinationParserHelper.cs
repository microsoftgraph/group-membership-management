using System;
using System.Text.Json;
namespace Models.Helpers
{
    public class DestinationParserHelper
    {
        enum MembershipType
        {
            GroupMembership,
            TeamsChannelMembership
        }
        public static DestinationObject ParseDestination(SyncJob syncJob)
        {
            if (string.IsNullOrWhiteSpace(syncJob.Destination)) return null;

            using JsonDocument doc = JsonDocument.Parse(syncJob.Destination);
            JsonElement rootElement = doc.RootElement[0];

            if (rootElement.ValueKind != JsonValueKind.Object) return null;

            JsonElement valueElement;
            if (!rootElement.TryGetProperty("value", out valueElement) ||
                !rootElement.TryGetProperty("type", out JsonElement typeElement) ||
                valueElement.ValueKind != JsonValueKind.Object ||
                !valueElement.TryGetProperty("objectId", out JsonElement objectIdElement) ||
                !Guid.TryParse(objectIdElement.GetString(), out Guid objectIdGuid))
            {
                return null;
            }

            string type = typeElement.GetString();

            if (type == MembershipType.TeamsChannelMembership.ToString())
            {
                if (!valueElement.TryGetProperty("channelId", out JsonElement channelIdElement)) return null;

                return new DestinationObject
                {
                    Type = type,
                    Value = new TeamsChannelDestinationValue
                    {
                        ObjectId = objectIdGuid,
                        ChannelId = channelIdElement.GetString()
                    }
                };
            }
            else if (type == MembershipType.GroupMembership.ToString())
            {
                return new DestinationObject
                {
                    Type = type,
                    Value = new GroupDestinationValue
                    {
                        ObjectId = objectIdGuid,
                    }
                };
            }
            else
            {
                return null;
            }
        }
    }
}

