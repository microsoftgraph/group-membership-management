// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IsHRSourcePartQuery, IsGroupMembershipSourcePartQuery, IsGroupOwnershipSourcePartQuery, ISourcePart } from '../models';
import { SourcePartQuery } from '../models/SourcePartQuery';
import { SourcePartType } from '../models/SourcePartType';

export function removeUnusedProperties<T extends SourcePartQuery>(sourcePart: T): T {
    if (IsHRSourcePartQuery(sourcePart)) {
        let trimmedSource = sourcePart;
        if (trimmedSource.source.manager === undefined || (trimmedSource.source.manager && trimmedSource.source.manager.id === undefined)) {
            trimmedSource = {
                ...sourcePart,
                source: {
                    filter: sourcePart.source.filter || undefined
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
                    filter: sourcePart.source.filter || undefined
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
