// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface IDataFactorySecret<TType>
    {
        string Pipeline { get; set; }       
        string DataFactoryName { get; set; }       
        string SubscriptionId { get; set; }
        string ResourceGroup { get; set; }

    }
}
