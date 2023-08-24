using System;

namespace Models
{
    public class DestinationValue
    {
        public virtual Guid ObjectId { get; set; }
    }

    public class TeamsChannelDestinationValue : DestinationValue
    {
        public override Guid ObjectId { get; set; }
        public string ChannelId { get; set; }

        public override string ToString()
        {
            return $"{{ ObjectId: {ObjectId}, ChannelId: {ChannelId} }}";
        }
    }

    public class GroupDestinationValue : DestinationValue
    {
        public override Guid ObjectId { get; set; }

        public override string ToString()
        {
            return $"{{ ObjectId: {ObjectId} }}";
        }
    }

    public class DestinationObject
    {
        public string Type { get; set; }
        public DestinationValue Value { get; set; }

    }

}

