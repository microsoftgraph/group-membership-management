// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Notifications;
using System;

namespace Hosts.AzureMaintenance
{
    public class PurgingEmailSenderRequest
    {
        public Guid RunId { get; set; }
        public PurgedSyncJob SyncJob { get; set; }
        public NotificationMessageType NotificationType { get; set; }

        // The original SyncJob.Id (pre-purge). PurgedSyncJob.Id is a new GUID,
        // so this is needed to look up SyncJobChanges (e.g. last SubmissionRejected
        // change) after the SyncJobs row has been deleted. SyncJobChanges has no
        // FK/cascade to SyncJobs, so historical rows survive the delete.
        public Guid OriginalSyncJobId { get; set; }
    }
}