// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    IsHRSourcePartQuery,
    IsGroupMembershipSourcePartQuery,
    IsGroupOwnershipSourcePartQuery,
    IsPlaceMembershipSourcePartQuery,
    ISourcePart,
} from '../models';
import { SourcePartQuery } from '../models/SourcePartQuery';
import { SourcePartType } from '../models/SourcePartType';
import { hasTrailingAndOrOperator, removeTrailingAndOrOperator } from './filterValidationHelpers';

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
                return hasManager || hasFilter;
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
        default:
            return false;
    }
 };
