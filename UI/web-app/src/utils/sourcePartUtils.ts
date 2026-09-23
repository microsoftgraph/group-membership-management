// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    IsHRSourcePartQuery,
    IsGroupMembershipSourcePartQuery,
    IsGroupOwnershipSourcePartQuery,
    IsPlaceMembershipSourcePartQuery,
    IsTeamsChannelMembershipSourcePartQuery,
    ISourcePart,
} from '../models';
import { SourcePartQuery } from '../models/SourcePartQuery';
import { SourcePartType } from '../models/SourcePartType';
import { hasTrailingAndOrOperator, hasValidEqualityOperators, hasValueAfterOperator, removeTrailingAndOrOperator } from './filterValidationHelpers';

export function removeUnusedProperties<T extends SourcePartQuery>(sourcePart: T): T {
    if (IsHRSourcePartQuery(sourcePart)) {
        let trimmedSource = sourcePart;

        // Clean the filter by removing trailing AND/OR operators if present
        let cleanedFilter = sourcePart.source.filter || undefined;
        if (cleanedFilter && hasTrailingAndOrOperator(cleanedFilter)) {
            cleanedFilter = removeTrailingAndOrOperator(cleanedFilter);
        }

        if (trimmedSource.source.manager === undefined || (trimmedSource.source.manager && trimmedSource.source.manager.id === undefined)) {
            trimmedSource = {
                ...sourcePart,
                source: {
                    filter: cleanedFilter
                },
            };
        }
        else {
            trimmedSource = {
                ...sourcePart,
                source: {
                    manager: {
                        id: sourcePart.source?.manager?.id ?? undefined,
                        depth: typeof sourcePart.source?.manager?.depth === 'number' && sourcePart.source.manager.depth > 0 ? sourcePart.source.manager.depth : sourcePart.source?.manager?.depth,
                      },
                    filter: cleanedFilter
                },
            };
        }
        return trimmedSource as T;
    } else if (IsGroupMembershipSourcePartQuery(sourcePart)) {
        // No properties to trim for GroupMembershipSourcePart
        return sourcePart;
    } else if (IsGroupOwnershipSourcePartQuery(sourcePart)) {
        // No properties to trim for GroupOwnershipSourcePart
        return sourcePart;
    } else if (IsPlaceMembershipSourcePartQuery(sourcePart)) {
        // No properties to trim for PlaceMembershipSourcePart
        return sourcePart;
    } else if (IsTeamsChannelMembershipSourcePartQuery(sourcePart)) {
        // No properties to trim for TeamsChannelMembershipSourcePart
        return sourcePart;
    } else {
        // During live editing (advanced view) or transitional states a source part can be incomplete.
        // Instead of throwing (which crashes the UI), return the original object unchanged.
        // eslint-disable-next-line no-console
        console.debug('removeUnusedProperties: unsupported or incomplete source part encountered, returning original.', sourcePart);
        return sourcePart;
    }
}

export function isSourcePartValid(sourcePart: ISourcePart): boolean {
    switch(sourcePart.query.type){
        case SourcePartType.HR:
            if (IsHRSourcePartQuery(sourcePart.query)) {
                const managerId = sourcePart.query.source?.manager?.id;
                const filter = sourcePart.query.source?.filter;
                const hasManager = Number.isFinite(managerId);
                const hasFilter = typeof filter === 'string' && filter.trim().length > 0;
                // A filter is only valid when every clause has a recognized equality operator AND a
                // real value after it. Checking the operator alone let `Attribute = ''` (a cleared
                // value box, which the builder still quotes) through to submission.
                const isFilterValid = hasFilter && hasValidEqualityOperators(filter) && hasValueAfterOperator(filter);
                // Valid if: has a valid filter, or has a manager (filter is optional when manager is set)
                // Invalid if: has a filter with missing operators or missing values (even with a
                // manager, a malformed filter should block)
                if (hasFilter && !isFilterValid) {
                    return false;
                }
                return hasManager || isFilterValid;
            }
            return false;
        case SourcePartType.GroupMembership:
            if (IsGroupMembershipSourcePartQuery(sourcePart.query)) {
                const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
                return guidRegex.test(sourcePart.query.source);
            }
            return false;
        case SourcePartType.GroupOwnership:
            if(IsGroupOwnershipSourcePartQuery(sourcePart.query)){
                return sourcePart.query.source.length > 0;
            }
            return false;
        case SourcePartType.PlaceMembership:
            if(IsPlaceMembershipSourcePartQuery(sourcePart.query)){
                return sourcePart.query.source.length > 0;
            }
            return false;
        case SourcePartType.TeamsChannelMembership:
            if(IsTeamsChannelMembershipSourcePartQuery(sourcePart.query)){
                return sourcePart.query.source.objectId.length > 0 && sourcePart.query.source.channelId.length > 0;
            }
            return false;
        default:
            return false;
    }
 };

// US3: a TeamsChannel source is only meaningful alongside at least one other (non-TeamsChannel)
// source part. Returns true when the parts contain a TeamsChannel source but no other source type,
// which the WebApi rejects on submit (TeamsChannelSourceRequiresAdditionalSource). Surfacing this in
// the UI lets the user fix it before submitting instead of only seeing a post-submit error.
export function hasStandaloneTeamsChannelSource(sourceParts: ISourcePart[]): boolean {
    const hasTeamsChannelSource = sourceParts.some(part => part.query?.type === SourcePartType.TeamsChannelMembership);
    if (!hasTeamsChannelSource) {
        return false;
    }
    const hasOtherSource = sourceParts.some(part => part.query?.type && part.query.type !== SourcePartType.TeamsChannelMembership);
    return !hasOtherSource;
}
