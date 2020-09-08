// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class LogAnalyticsSecret<T> : ILogAnalyticsSecret<T>
    {
        public string WorkSpaceId { get; }
        public string SharedKey { get; }
        public string Location { get; }
        public LogAnalyticsSecret(string workSpaceId, string sharedKey, string location)
        {
            WorkSpaceId = workSpaceId;
            SharedKey = sharedKey;
            Location = location;
        }
    }
}
