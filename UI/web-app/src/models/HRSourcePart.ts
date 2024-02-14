// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartType } from "./ISourcePart";

export type HRSourcePart = {
    type: SourcePartType.HR;
    source: HRSourcePartSource;
    exclusionary?: boolean;
  };

export type HRSourcePartSource = {
    ids?: number[];
    filter?: string;
    depth?: number;
    includeOrg?: boolean;
    includeFilter?: boolean;
};
