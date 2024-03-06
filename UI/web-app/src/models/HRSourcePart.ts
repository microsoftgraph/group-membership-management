// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartQuery } from "./SourcePartQuery";
import { SourcePartType } from "./SourcePartType";

export type HRSourcePart = {
    type: SourcePartType.HR;
    source: HRSourcePartSource;
    exclusionary?: boolean;
  };

  export type HRSourcePartSource = {
    manager?: {
      id?: number;
      depth?: number;
    };    
    filter?: string;
};

export const IsHRSourcePartQuery = (query: SourcePartQuery): query is HRSourcePart => {
  return query.type === SourcePartType.HR;
}