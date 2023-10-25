// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
namespace SqlMembershipObtainer.Common.DependencyInjection
{
    public interface ISqlMembershipObtainerServiceSecret
    {
        bool SkipIfUsersArentFound { get; }
        string SqlServerConnectionString { get; }

    }
}
