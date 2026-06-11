// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.SyncJobChange;
using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetJobChangesRequest : RequestBase
    {

        public GetJobChangesRequest(Guid syncJobId)
        {
            SyncJobId = syncJobId;
        }

        public Guid SyncJobId { get; }
        public int StartPage { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public SyncJobChangeSortingField SortBy { get; set; } = SyncJobChangeSortingField.ChangeTime;
        public bool SortAscending { get; set; } = false;
        public string? ChangeReason { get; set; }
    }
}
