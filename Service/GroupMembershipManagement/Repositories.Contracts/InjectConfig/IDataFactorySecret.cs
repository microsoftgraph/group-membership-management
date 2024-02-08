// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IDataFactorySecret<TType>
    {
        string Pipeline { get; set; }
        string TenantId { get; set; }
        string DataFactoryName { get; set; }
        string SqlMembershipAppId { get; set; }
        string SqlMembershipAppAuthenticationKey { get; set; }
        string SubscriptionId { get; set; }
        string ResourceGroup { get; set; }

    }
}
