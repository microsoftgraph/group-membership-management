// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
namespace SqlMembershipObtainer.Common.DependencyInjection
{
    public class DataFactorySecrets<T> : IDataFactorySecret<T>
    {
        public string Pipeline { get; }
        public string TenantId { get; }
        public string DataFactoryName { get; }
        public string SqlMembershipAppId { get; }
        public string SqlMembershipAppAuthenticationKey { get; }
        public string SubscriptionId { get; }
        public string ResourceGroup { get; }

        public DataFactorySecrets(string pipeline, string tenantId, string dataFactoryName, string sqlMembershipAppId, string sqlMembershipAppAuthenticationKey, string subscriptionId, string resourceGroup)
        {
            Pipeline = pipeline;
            TenantId = tenantId;
            DataFactoryName = dataFactoryName;
            SqlMembershipAppId = sqlMembershipAppId;
            SqlMembershipAppAuthenticationKey = sqlMembershipAppAuthenticationKey;
            SubscriptionId = subscriptionId;
            ResourceGroup = resourceGroup;
        }
    }
}
