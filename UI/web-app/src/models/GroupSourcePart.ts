// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type GroupSourcePart = {
    type: 'GroupMembership';
    source: string;
    exclusionary?: boolean;
};