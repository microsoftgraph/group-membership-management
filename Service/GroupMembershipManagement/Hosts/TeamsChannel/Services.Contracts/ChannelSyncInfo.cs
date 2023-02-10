using Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeamsChannel.Service.Contracts
{
    // this should be easy to turn into one of those durable function Request objects later
    public class ChannelSyncInfo
    {
        public SyncJob SyncJob { get; init; }
        public int TotalParts { get; init; }
        public int CurrentPart { get; init; }
        public bool Exclusionary { get; init; }
        public bool IsDestinationPart { get; init; }
    }
}
