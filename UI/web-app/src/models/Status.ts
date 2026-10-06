// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export enum SyncStatus {
  ThresholdExceeded = 'ThresholdExceeded',
  CustomerPaused = 'CustomerPaused',
  DeveloperPaused = 'DeveloperPaused',
  MembershipDataNotFound = 'MembershipDataNotFound',
  DestinationGroupNotFound = 'DestinationGroupNotFound',
  NotOwnerOfDestinationGroup = 'NotOwnerOfDestinationGroup',
  SourceGroupNotFound = 'SourceGroupNotFound',
  ManagerNotFound = 'ManagerNotFound',
  Idle = 'Idle',
  InProgress = 'InProgress',
  PendingReview = 'PendingReview',
  PendingConfiguration = 'PendingConfiguration',
  PendingAutoApproval = 'PendingAutoApproval',
  SubmissionRejected = 'SubmissionRejected',
  NestedGroupsFound = 'NestedGroupsFound',
  GuestUsersCannotBeAddedToUnifiedGroup = 'GuestUsersCannotBeAddedToUnifiedGroup'
}

export enum ActionRequired {
  ThresholdExceeded = 'Threshold Exceeded',
  Paused = 'Paused',
  DeveloperPaused = 'System Paused',
  MembershipDataNotFound = 'No Users Found In Source',
  DestinationGroupNotFound = 'Destination Group Not Found',
  NotOwnerOfDestinationGroup = 'Group Owner Permission Required',
  SourceGroupNotFound = 'Source Group Not Found',
  ManagerNotFound = 'Source Manager Not Found',
  PendingReview = 'Pending Review',
  PendingConfiguration = 'System Configuration in Progress',
  PendingAutoApproval = 'Pending Automated Review',
  SubmissionRejected = 'Submission Rejected',
  NestedGroupsFound = 'Nested Groups Detected',
  GuestUsersCannotBeAddedToUnifiedGroup = 'Guest Users Not Supported'
};

export enum RunHistoryStatus {
  Idle = 'Idle',
  Error = 'Error',
  ErroredDueToStuckInProgress = 'ErroredDueToStuckInProgress',
  QueryNotValid = 'QueryNotValid',
  DestinationQueryNotValid = 'DestinationQueryNotValid',
  FileNotFound = 'FileNotFound',
  FilePathNotValid = 'FilePathNotValid',
  SchemaError = 'SchemaError',
  TransientError = 'TransientError',
  DestinationGroupNotFound = 'DestinationGroupNotFound',
  SourceGroupNotFound = 'SourceGroupNotFound',
  ManagerNotFound = 'ManagerNotFound',
  MembershipDataNotFound = 'MembershipDataNotFound',
  NotOwnerOfDestinationGroup = 'NotOwnerOfDestinationGroup',
  GuestUsersCannotBeAddedToUnifiedGroup = 'GuestUsersCannotBeAddedToUnifiedGroup',
  ThresholdExceeded = 'ThresholdExceeded'
};
