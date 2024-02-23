// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartQuery } from "./SourcePartQuery";
import { SourcePartType } from "./SourcePartType";

export type GroupOwnershipSourcePart = {
    type: SourcePartType.GroupOwnership;
    source: string[];
    exclusionary?: boolean;
};

export const IsGroupOwnershipSourcePartQuery = (query: SourcePartQuery): query is GroupOwnershipSourcePart => {
    return query.type === SourcePartType.GroupOwnership;
}
