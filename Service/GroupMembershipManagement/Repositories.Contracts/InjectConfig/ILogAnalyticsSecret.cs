// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface ILogAnalyticsSecret<TType>
    {        
        string WorkSpaceId { get; }
        string SharedKey { get; }
        string Location { get; }
    }
}
