// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.Entities
{
    public class OperationsSettings
    {
        public string ServiceBusFQN { get; set; }
        public string SyncJobTopic { get; set; }
        public string MembershipUpdatersTopic { get; set; }
        public string MembershipAggregatorQueue { get; set; }
        public string PendingConfigurationQueue { get; set; }
        public string NotificationsQueue { get; set; }
        public string JobSchedulerFunctionKey { get; set; }
        public string JobSchedulerFunctionBaseUrl { get; set; }
        public string DataResourceGroupName { get; set; }
        public string ComputeResourceGroupName { get; set; }
        public string FunctionAuthAppClientId { get; set; }
        public string FunctionsStorageAccountName { get; set; }

    }
}
