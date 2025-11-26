// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { GroupMember } from './GroupMember';

export interface GetGroupMembersResponse {
  groupId: string;
  groupMemberCount: number;
  groups: GroupMember[];
}