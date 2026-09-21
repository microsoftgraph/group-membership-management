// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.DestinationResolution;
using Repositories.EntityFramework.Contexts;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.EntityFramework
{
    /// <summary>
    /// EF-entity implementation of <see cref="IDestinationResolver"/> that reads typed
    /// destination identity from the Table-Per-Type model (<c>Destinations</c> plus the
    /// matching <c>GroupDestinations</c> / <c>TeamsChannelDestinations</c> row) using
    /// no-tracking <see cref="GMMReadContext"/> projections (contract:
    /// resolver-json-cutover.md Section Resolver behavior, FR-015/FR-016/FR-017).
    ///
    /// It returns the same typed results as <see cref="LegacyDestinationResolver"/> for
    /// equivalent records, rejects missing / wrong-type / both-type structures, and never
    /// silently falls back per job. Object-ID lookup relies on the indexed per-type columns.
    /// This resolver is registered in place of the legacy resolver only after reconciliation
    /// readiness (feature-flag controlled); it does not change legacy writes.
    /// </summary>
    public class DestinationEntityResolver : IDestinationResolver
    {
        private readonly GMMReadContext _readContext;

        public DestinationEntityResolver(GMMReadContext readContext)
        {
            _readContext = readContext ?? throw new ArgumentNullException(nameof(readContext));
        }

        public async Task<ResolvedDestination> ResolveAsync(SyncJob syncJob, CancellationToken cancellationToken = default)
        {
            if (syncJob == null)
            {
                return null;
            }

            var syncJobId = syncJob.Id;

            var baseRow = await _readContext.Destinations
                .AsNoTracking()
                .SingleOrDefaultAsync(d => d.SyncJobId == syncJobId, cancellationToken);

            // No consolidated base row: not eligible for a consolidated read (no per-job fallback).
            if (baseRow == null || string.IsNullOrEmpty(baseRow.DestinationType))
            {
                return null;
            }

            var group = await _readContext.GroupDestinations
                .AsNoTracking()
                .SingleOrDefaultAsync(g => g.SyncJobId == syncJobId, cancellationToken);

            var channel = await _readContext.TeamsChannelDestinations
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.SyncJobId == syncJobId, cancellationToken);

            // A base row must have exactly one matching per-type row; both is a structural violation.
            if (group != null && channel != null)
            {
                return null;
            }

            if (string.Equals(baseRow.DestinationType, MembershipTypes.GroupMembership.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (group == null)
                {
                    return null;
                }

                return new ResolvedGroupDestination
                {
                    SyncJobId = syncJobId,
                    ObjectId = group.GroupId
                };
            }

            if (string.Equals(baseRow.DestinationType, MembershipTypes.TeamsChannelMembership.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (channel == null || string.IsNullOrEmpty(channel.ChannelId))
                {
                    return null;
                }

                return new ResolvedTeamsChannelDestination
                {
                    SyncJobId = syncJobId,
                    TeamObjectId = channel.TeamId,
                    ChannelId = channel.ChannelId
                };
            }

            return null;
        }
    }
}
