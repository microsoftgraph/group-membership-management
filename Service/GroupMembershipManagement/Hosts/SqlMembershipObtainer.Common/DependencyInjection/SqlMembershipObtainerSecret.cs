// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
namespace SqlMembershipObtainer.Common.DependencyInjection
{
    public class SqlMembershipObtainerSecret<T> : ISqlMembershipObtainerSecret<T>
    {
        public string SqlMembershipObtainerStorageAccountName { get; }
        public string SqlMembershipObtainerStorageAccountConnectionString { get; }

        public SqlMembershipObtainerSecret(string sqlMembershipObtainerStorageAccountName, string sqlMembershipObtainerStorageAccountConnectionString)
        {
            SqlMembershipObtainerStorageAccountName = sqlMembershipObtainerStorageAccountName;
            SqlMembershipObtainerStorageAccountConnectionString = sqlMembershipObtainerStorageAccountConnectionString;
        }
    }
}
