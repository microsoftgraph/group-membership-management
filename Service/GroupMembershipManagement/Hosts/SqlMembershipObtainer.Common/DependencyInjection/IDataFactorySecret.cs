// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
namespace SqlMembershipObtainer.Common.DependencyInjection
{
    public interface IDataFactorySecret<TType>
    {
        string Pipeline { get; }
        string TenantId { get; }
        string DataFactoryName { get; }
        string SqlMembershipAppId { get; }
        string SqlMembershipAppAuthenticationKey { get; }
        string SubscriptionId { get; }
        string ResourceGroup { get; }

    }
}
