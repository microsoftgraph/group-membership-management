// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export enum MembershipChangeType {
  Added = "Added",
  Removed = "Removed",
}

export interface SearchSyncHistoryByUserRunMembershipChange {
  runId: string;
  membershipChangeType: MembershipChangeType;
}

export interface SearchSyncHistoryByUserResult {
  matchingRunIds: string[];
  runMembershipChanges: SearchSyncHistoryByUserRunMembershipChange[];
  userInCurrentGroup: boolean;
  checkedCurrentGroupMembership: boolean;
}