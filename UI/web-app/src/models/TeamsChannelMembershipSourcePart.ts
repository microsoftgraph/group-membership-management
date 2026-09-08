// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartQuery } from './SourcePartQuery';
import { SourcePartType } from './SourcePartType';

export type TeamsChannelMembershipSource = {
    objectId: string;
    channelId: string;
};

export type TeamsChannelMembershipSourcePart = {
    type: SourcePartType.TeamsChannelMembership;
    source: TeamsChannelMembershipSource;
    exclusionary?: boolean;
};

export const IsTeamsChannelMembershipSourcePartQuery = (query: SourcePartQuery): query is TeamsChannelMembershipSourcePart => {
    return query.type === SourcePartType.TeamsChannelMembership;
};

export type TeamsChannelSourceDisplayNames = { teamName: string; channelName: string };

// Resolves the team/channel display names for a TeamsChannel source part. US3 requires a
// TeamsChannel source to be identical to the job's TeamsChannel destination, so the friendly
// names are only surfaced when the destination's ids actually match the part's stored source ids.
// On any mismatch (e.g. a transient state mid destination-change) we fall back to the raw ids the
// part will actually be saved with, so the UI never shows a team/channel that differs from what
// is persisted.
export const resolveTeamsChannelSourceDisplayNames = (
    source: TeamsChannelMembershipSource | undefined,
    destination: { id?: string; name?: string; channelId?: string; channelName?: string; type?: string } | undefined
): TeamsChannelSourceDisplayNames => {
    const equalsIgnoreCase = (a?: string, b?: string): boolean => (a ?? '').toLowerCase() === (b ?? '').toLowerCase();
    const matchesDestination =
        !!source &&
        !!destination &&
        destination.type === SourcePartType.TeamsChannelMembership &&
        equalsIgnoreCase(destination.id, source.objectId) &&
        equalsIgnoreCase(destination.channelId, source.channelId);
    return {
        teamName: matchesDestination ? (destination?.name ?? '') : (source?.objectId ?? ''),
        channelName: matchesDestination ? (destination?.channelName ?? '') : (source?.channelId ?? ''),
    };
};
