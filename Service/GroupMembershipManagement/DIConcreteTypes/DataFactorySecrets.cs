// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class DataFactorySecrets<T> : IDataFactorySecret<T>
    {
        public string Pipeline { get; }
        public string DataFactoryName { get; }
        public string SubscriptionId { get; }
        public string ResourceGroup { get; }

        public DataFactorySecrets(string pipeline, string dataFactoryName, string subscriptionId, string resourceGroup)
        {
            Pipeline = pipeline;
            DataFactoryName = dataFactoryName;
            SubscriptionId = subscriptionId;
            ResourceGroup = resourceGroup;
        }
    }
}
