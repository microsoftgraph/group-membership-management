// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.SyncJobChange;

namespace WebApi.Models.DTOs
{
    public class SyncJobChangeDTO
    {
        public SyncJobChangeDTO()
        {
        }

        public Guid Id { get; set; }
        public Guid? SyncJobId { get; set; }
        public DateTime ChangeTime { get; set; }
        public string ChangedByDisplayName { get; set; }
        public Guid ChangedByObjectId { get; set; }
        public SyncJobChangeSource ChangeSource { get; set; }
        public string ChangeReason { get; set; }
        public string ChangeDetails { get; set; }
    }
}
