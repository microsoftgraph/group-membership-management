// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { HRSourcePart } from "./HRSourcePart";
import { GroupOwnershipSourcePart } from "./GroupOwnershipSourcePart";
import { GroupMembershipSourcePart } from "./GroupMembershipSourcePart";
import { PlaceMembershipSourcePart } from "./PlaceMembershipSourcePart";

export type SourcePartQuery = HRSourcePart | GroupMembershipSourcePart | GroupOwnershipSourcePart | PlaceMembershipSourcePart;
