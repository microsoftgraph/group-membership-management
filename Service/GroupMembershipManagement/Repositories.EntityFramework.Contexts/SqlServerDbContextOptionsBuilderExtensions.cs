// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Repositories.EntityFramework.Contexts
{
    /// <summary>
    /// Provides the SQL Server retry configuration shared by GMM database contexts.
    /// </summary>
    public static class SqlServerDbContextOptionsBuilderExtensions
    {
        private const int ConnectionAttemptTimedOutErrorNumber = 258;
        private const int MaxRetryCount = 1;

        private static readonly int[] _additionalTransientErrorNumbers =
        [
            ConnectionAttemptTimedOutErrorNumber
        ];

        private static readonly TimeSpan _maxRetryDelay = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Enables EF Core's SQL Server retry strategy and also retries connection-opening timeouts.
        /// </summary>
        /// <param name="optionsBuilder">The SQL Server options builder to configure. It cannot be <see langword="null"/>.</param>
        /// <returns>The same options builder so that additional SQL Server options can be configured.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="optionsBuilder"/> is <see langword="null"/>.</exception>
        public static SqlServerDbContextOptionsBuilder EnableGmmRetryOnFailure(
            this SqlServerDbContextOptionsBuilder optionsBuilder)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);

            return optionsBuilder.EnableRetryOnFailure(
                MaxRetryCount,
                _maxRetryDelay,
                _additionalTransientErrorNumbers);
        }
    }
}
