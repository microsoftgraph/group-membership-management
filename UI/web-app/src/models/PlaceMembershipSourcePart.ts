// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartQuery } from "./SourcePartQuery";
import { SourcePartType } from "./SourcePartType";

export type PlaceMembershipSourcePart = {
    type: SourcePartType.PlaceMembership;
    source: string;
    exclusionary?: boolean;
};

export const IsPlaceMembershipSourcePartQuery = (query: SourcePartQuery): query is PlaceMembershipSourcePart => {
    return query.type === SourcePartType.PlaceMembership;
}
