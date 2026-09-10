// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.AgentReader.Services.Entities
{
    public sealed class AgentReadCounters
    {
        public long TotalEnumerated { get; set; }
        public long Inserted { get; set; }
        public long FilterPassingAgents { get; set; }
        public long FilteredNoManager { get; set; }

        // Keep the specified counter names; the source is manager.onPremisesImmutableId, not employeeId.
        public long FilteredManagerEmployeeIdAbsent { get; set; }
        public long FilteredManagerEmployeeIdNonNumeric { get; set; }
        public long FilteredManagerEmployeeIdOutOfRange { get; set; }
        public long FilteredInvalidAgentObjectId { get; set; }
        public long AccountEnabledDefaultApplied { get; set; }
        public long BlueprintIdTruncated { get; set; }

        public long TotalFiltered => checked(
            FilteredNoManager
            + FilteredManagerEmployeeIdAbsent
            + FilteredManagerEmployeeIdNonNumeric
            + FilteredManagerEmployeeIdOutOfRange
            + FilteredInvalidAgentObjectId);

        public void Add(AgentReadCounters other)
        {
            checked
            {
                TotalEnumerated += other.TotalEnumerated;
                Inserted += other.Inserted;
                FilterPassingAgents += other.FilterPassingAgents;
                FilteredNoManager += other.FilteredNoManager;
                FilteredManagerEmployeeIdAbsent += other.FilteredManagerEmployeeIdAbsent;
                FilteredManagerEmployeeIdNonNumeric += other.FilteredManagerEmployeeIdNonNumeric;
                FilteredManagerEmployeeIdOutOfRange += other.FilteredManagerEmployeeIdOutOfRange;
                FilteredInvalidAgentObjectId += other.FilteredInvalidAgentObjectId;
                AccountEnabledDefaultApplied += other.AccountEnabledDefaultApplied;
                BlueprintIdTruncated += other.BlueprintIdTruncated;
            }
        }
    }
}
