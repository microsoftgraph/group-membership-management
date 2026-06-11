// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export enum SyncStatus {
  ThresholdExceeded = 'ThresholdExceeded',
  CustomerPaused = 'CustomerPaused',
  DeveloperPaused = 'DeveloperPaused',
  MembershipDataNotFound = 'MembershipDataNotFound',
  DestinationGroupNotFound = 'DestinationGroupNotFound',
  NotOwnerOfDestinationGroup = 'NotOwnerOfDestinationGroup',
  SecurityGroupNotFound = 'SecurityGroupNotFound',
  Idle = 'Idle',
  InProgress = 'InProgress',
  PendingReview = 'PendingReview',
  PendingConfiguration = 'PendingConfiguration',
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
  SecurityGroupNotFound = 'Source Group Not Found',
  PendingReview = 'Pending Review',
  PendingConfiguration = 'System Configuration in Progress',
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
  SecurityGroupNotFound = 'SecurityGroupNotFound',
  MembershipDataNotFound = 'MembershipDataNotFound',
  NotOwnerOfDestinationGroup = 'NotOwnerOfDestinationGroup',
  GuestUsersCannotBeAddedToUnifiedGroup = 'GuestUsersCannotBeAddedToUnifiedGroup',
  ThresholdExceeded = 'ThresholdExceeded'
};
