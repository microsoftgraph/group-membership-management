// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
namespace SqlMembershipObtainer.Common.DependencyInjection
{
    public class SqlMembershipObtainerServiceSecret : ISqlMembershipObtainerServiceSecret
    {
        public bool SkipIfUsersArentFound { get; }
        public string SqlServerConnectionString { get; }

        public SqlMembershipObtainerServiceSecret(bool skipIfUsersArentFound, string sqlServerConnectionString)
        {
            this.SkipIfUsersArentFound = skipIfUsersArentFound;
            SqlServerConnectionString = sqlServerConnectionString;
        }

        public SqlMembershipObtainerServiceSecret()
        {

        }
    }
}
