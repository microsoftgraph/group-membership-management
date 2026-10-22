// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartQuery } from './SourcePartQuery';
import { SourcePartType } from './SourcePartType';
import type { IdentityType } from './HRSourcePart';

export type GroupMembershipSourcePart = {
    type: SourcePartType.GroupMembership;
    source: string;
    exclusionary?: boolean;
    identityTypes?: IdentityType[];
};

export const IsGroupMembershipSourcePartQuery = (query: SourcePartQuery): query is GroupMembershipSourcePart => {
    return query.type === SourcePartType.GroupMembership;
};
