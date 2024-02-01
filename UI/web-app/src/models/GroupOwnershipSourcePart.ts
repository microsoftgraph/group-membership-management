// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type GroupOwnershipSourcePart = {
    type: 'GroupOwnership';
    source: string[];
    exclusionary?: boolean;
};