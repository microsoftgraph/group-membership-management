// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
namespace SqlMembershipObtainer.Common.DependencyInjection
{
    public interface ISqlMembershipObtainerSecret<T>
    {
        string SqlMembershipObtainerStorageAccountName { get; }
        string SqlMembershipObtainerStorageAccountConnectionString { get; }

    }
}
