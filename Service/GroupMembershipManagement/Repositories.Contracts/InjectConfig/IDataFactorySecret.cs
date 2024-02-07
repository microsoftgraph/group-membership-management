// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IDataFactorySecret<TType>
    {
        string Pipeline { get; }
        string DataFactoryName { get; }
        string SubscriptionId { get; }
        string ResourceGroup { get; }

    }
}
