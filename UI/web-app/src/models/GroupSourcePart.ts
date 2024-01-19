// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type GroupSourcePart = {
    type: 'GroupMembership';
    source: GroupSourcePartSource;
    exclusionary?: boolean;
};

export type GroupSourcePartSource = {
    source: string;
};