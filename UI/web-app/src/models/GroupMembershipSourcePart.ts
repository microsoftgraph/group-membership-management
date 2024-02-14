// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartType } from "./ISourcePart";

export type GroupMembershipSourcePart = {
    type: SourcePartType.GroupMembership;
    source: string;
    exclusionary?: boolean;
};