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
  SubmissionRejected = 'SubmissionRejected'
}

export enum ActionRequired {
  ThresholdExceeded = 'Threshold Exceeded',
  Paused = 'Paused',
  DeveloperPaused = 'Developer Paused',
  MembershipDataNotFound = 'No users in the source',
  DestinationGroupNotFound = 'Destination Group Not Found',
  NotOwnerOfDestinationGroup = 'Not Owner Of Destination Group',
  SecurityGroupNotFound = 'Security Group Not Found',
  PendingReview = 'Pending Review',
  PendingConfiguration = 'Pending Configuration',
  SubmissionRejected = 'Submission Rejected',
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
