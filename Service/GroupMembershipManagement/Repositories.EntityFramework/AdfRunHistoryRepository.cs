// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.AdfRunHistory;
using Repositories.Contracts;
using Repositories.EntityFramework.AdfContexts;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.EntityFramework
{
    /// <summary>
    /// Resolves the latest eligible ADF run from the ADF database using
    /// <see cref="AdfContext"/>.
    /// </summary>
    public sealed class AdfRunHistoryRepository : IAdfRunHistoryRepository
    {
        // Schema holding the published users tables; matches the [users] schema used by SqlMembershipRepository.
        private const string UsersSchemaPrefix = "users.";

        private readonly AdfContext _context;
        private readonly ILogger<AdfRunHistoryRepository> _logger;

        public AdfRunHistoryRepository(
            AdfContext context,
            ILogger<AdfRunHistoryRepository> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AdfRunHistory?> GetLatestAdfRunAsync(
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var adfRun = await _context.AdfRunHistory
                    .AsNoTracking()
                    .Where(run => AdfContext.ObjectId(UsersSchemaPrefix + run.AdfRunId.ToString().Replace("-", ""), "U") != null)
                    .OrderByDescending(run => run.PublishedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (adfRun is null)
                {
                    await LogNoEligibleRunAsync(correlationId, cancellationToken);
                    return null;
                }

                adfRun.PublishedAtUtc = DateTime.SpecifyKind(
                    adfRun.PublishedAtUtc,
                    DateTimeKind.Utc);

                _logger.LogInformation(
                    "Selected ADF run {AdfRunId}. CorrelationId: {CorrelationId}",
                    adfRun.AdfRunId,
                    correlationId);

                return adfRun;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to select the latest eligible ADF run. CorrelationId: {CorrelationId}",
                    correlationId);
                throw;
            }
        }

        // Distinguishes "nothing was ever recorded" from "runs were recorded but their snapshot
        // tables are missing" so operators know where to look. Diagnostics must never mask the
        // original null result.
        private async Task LogNoEligibleRunAsync(
            string? correlationId,
            CancellationToken cancellationToken)
        {
            try
            {
                var latestRecorded = await _context.AdfRunHistory
                    .AsNoTracking()
                    .OrderByDescending(run => run.PublishedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (latestRecorded is null)
                {
                    _logger.LogWarning(
                        "No eligible ADF run: [dbo].[AdfRunHistory] contains no rows, so no ADF run has been recorded as published. " +
                        "Verify that the ADF pipeline's final 'Record ADF Run History' activity has completed successfully in this environment. " +
                        "CorrelationId: {CorrelationId}",
                        correlationId);
                    return;
                }

                var recordedCount = await _context.AdfRunHistory
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                _logger.LogWarning(
                    "No eligible ADF run: [dbo].[AdfRunHistory] contains {RecordedRunCount} run(s) but none has a published snapshot table in the 'users' schema. " +
                    "Latest recorded run is {LatestAdfRunId} (recorded {LatestPublishedAtUtc:O}). " +
                    "The snapshot tables may have been removed by cleanup, or the run was recorded before its snapshot table was created. " +
                    "CorrelationId: {CorrelationId}",
                    recordedCount,
                    latestRecorded.AdfRunId,
                    DateTime.SpecifyKind(latestRecorded.PublishedAtUtc, DateTimeKind.Utc),
                    correlationId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "No eligible ADF run was found, and the follow-up diagnostic against [dbo].[AdfRunHistory] failed. CorrelationId: {CorrelationId}",
                    correlationId);
            }
        }
    }
}
