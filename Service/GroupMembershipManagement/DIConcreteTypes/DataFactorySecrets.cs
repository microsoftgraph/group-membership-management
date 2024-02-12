// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class DataFactorySecrets<T> : IDataFactorySecret<T>
    {
        public string Pipeline { get; set; }
        public string DataFactoryName { get; set; }
        public string SubscriptionId { get; set; }
        public string ResourceGroup { get; set; }

        public DataFactorySecrets(string pipeline, string dataFactoryName, string subscriptionId, string resourceGroup)
        {
            Pipeline = pipeline;
            DataFactoryName = dataFactoryName;
            SubscriptionId = subscriptionId;
            ResourceGroup = resourceGroup;
        }

        public DataFactorySecrets()
        {
        }
    }
}
