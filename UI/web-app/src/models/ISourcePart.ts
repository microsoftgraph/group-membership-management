// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { HRSourcePart } from "./HRSourcePart";
import { GroupOwnershipSourcePart } from "./GroupOwnershipSourcePart";
import { GroupMembershipSourcePart } from "./GroupMembershipSourcePart";

export enum SourcePartType {
    HR = "SqlMembership",
    GroupMembership = "GroupMembership",
    GroupOwnership = "GroupOwnership"
}

export type SourcePartQuery = HRSourcePart | GroupMembershipSourcePart | GroupOwnershipSourcePart;

export type ISourcePart = {
    id: number;
    query: SourcePartQuery;
    isValid: boolean;
};

export const IsHRSourcePartQuery = (query: SourcePartQuery): query is HRSourcePart => {
    return query.type === SourcePartType.HR;
}

export const IsGroupMembershipSourcePartQuery = (query: SourcePartQuery): query is GroupMembershipSourcePart => {
    return query.type === SourcePartType.GroupMembership;
}

export const IsGroupOwnershipSourcePartQuery = (query: SourcePartQuery): query is GroupOwnershipSourcePart => {
    return query.type === SourcePartType.GroupOwnership;
}

export const placeholderQueryHRPart: ISourcePart = {
    id: 1,
    query: {
        type: SourcePartType.HR,
        source: {
            ids: [],
            filter: "",
            depth: undefined
        },
        exclusionary: false
    },
    isValid: true
};

export const placeholderQueryGroupMembershipPart: ISourcePart = {
    id: 2,
    query: {
        type: SourcePartType.GroupMembership,
        source: "guid",
        exclusionary: false
    },
    isValid: true
};

export const placeholderQueryGroupOwnershipPart: ISourcePart = {
    id: 3,
    query: {
        type: SourcePartType.GroupOwnership,
        source: ["guid"],
        exclusionary: false
    },
    isValid: true
};
