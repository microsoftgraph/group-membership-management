// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IsHRSourcePartQuery, IsGroupMembershipSourcePartQuery, IsGroupOwnershipSourcePartQuery, ISourcePart } from '../models';
import { SourcePartQuery } from '../models/SourcePartQuery';
import { SourcePartType } from '../models/SourcePartType';

export function removeUnusedProperties<T extends SourcePartQuery>(sourcePart: T): T {
    if (IsHRSourcePartQuery(sourcePart)) {
        const trimmedSource = {
            ...sourcePart,
            source: {
                ...sourcePart.source,
                ids: sourcePart.source.ids?.length ? sourcePart.source.ids : undefined,
                filter: sourcePart.source.filter || undefined,
                depth: typeof sourcePart.source.depth === 'number' && sourcePart.source.depth > 0 ? sourcePart.source.depth : sourcePart.source.depth,
                includeOrg: sourcePart.source.includeOrg ?? undefined,
                includeFilter: sourcePart.source.includeFilter ?? undefined,
            },
        };
        return trimmedSource as T;
    } else if (IsGroupMembershipSourcePartQuery(sourcePart)) {
        // No properties to trim for GroupMembershipSourcePart
        return sourcePart;
    } else if (IsGroupOwnershipSourcePartQuery(sourcePart)) {
        // No properties to trim for GroupOwnershipSourcePart
        return sourcePart;
    } else {
        throw new Error("Not a supported source type.");
    }
}

export function isSourcePartValid(sourcePart: ISourcePart): boolean {
    switch(sourcePart.query.type){
        case SourcePartType.HR:
            if(IsHRSourcePartQuery(sourcePart.query)){
                return true;
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
        default:
            return false;
    }
 }
