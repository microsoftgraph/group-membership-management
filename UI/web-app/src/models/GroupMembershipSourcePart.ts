// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartQuery } from "./SourcePartQuery";
import { SourcePartType } from "./SourcePartType";

export type GroupMembershipSourcePart = {
    type: SourcePartType.GroupMembership;
    source: string;
    exclusionary?: boolean;
};

export const IsGroupMembershipSourcePartQuery = (query: SourcePartQuery): query is GroupMembershipSourcePart => {
    return query.type === SourcePartType.GroupMembership;
}