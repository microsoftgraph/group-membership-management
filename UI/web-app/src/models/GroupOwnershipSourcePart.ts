// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type GroupOwnershipSourcePart = {
    type: 'GroupOwnership';
    source: GroupOwnershipPartSource;
    exclusionary?: boolean;
};

export type GroupOwnershipPartSource = {
    source: string[];
};