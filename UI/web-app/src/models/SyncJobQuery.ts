// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { HRSourcePart } from "./HRSourcePart";
import { GroupOwnershipSourcePart } from "./GroupOwnershipSourcePart";
import { GroupSourcePart } from "./GroupSourcePart";

export type SyncJobQuery = (HRSourcePart | GroupSourcePart | GroupOwnershipSourcePart)[];