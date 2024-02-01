// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { HRSourcePart } from "./HRSourcePart";
import { GroupOwnershipSourcePart } from "./GroupOwnershipSourcePart";
import { GroupSourcePart } from "./GroupSourcePart";

export type SourcePartQuery = HRSourcePart | GroupSourcePart | GroupOwnershipSourcePart;

export type ISourcePart = {
    id: number;
    query: HRSourcePart | GroupSourcePart | GroupOwnershipSourcePart;
    isValid: boolean;
};

export const placeholderQueryHRPart: ISourcePart = {
    id: 1,
    query: {
        type: "SqlMembership",
        source: {
            ids: [],
            filter: "",
            depth: 1
        },
        exclusionary: false
    },
    isValid: true
};

export const placeholderQueryGroupPart: ISourcePart = {
    id: 2,
    query: {
        type: "GroupMembership",
        source: "guid",
        exclusionary: false
    },
    isValid: true
};

export const placeholderQueryGroupOwnershipPart: ISourcePart = {
    id: 3,
    query: {
        type: "GroupOwnership",
        source: ["guid"],
        exclusionary: false
    },
    isValid: true
};