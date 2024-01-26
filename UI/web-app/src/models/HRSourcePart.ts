// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type HRSourcePart = {
    type: 'SqlMembership';
    source: HRSourcePartSource;
    exclusionary?: boolean;
  };

export type HRSourcePartSource = {
    ids?: number[];
    filter?: string;
    depth?: number;
};
