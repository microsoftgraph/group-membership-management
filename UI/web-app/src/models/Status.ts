// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export enum SyncStatus {
  ThresholdExceeded = 'ThresholdExceeded',
  CustomerPaused = 'CustomerPaused',
  MembershipDataNotFound = 'MembershipDataNotFound',
  DestinationGroupNotFound = 'DestinationGroupNotFound',
  NotOwnerOfDestinationGroup = 'NotOwnerOfDestinationGroup',
  SecurityGroupNotFound = 'SecurityGroupNotFound',
  Idle = 'Idle',
  InProgress = 'InProgress',
  PendingReview = 'PendingReview',
  SubmissionRejected = 'SubmissionRejected',
  Removed = 'Removed'
}

export enum ActionRequired {
  ThresholdExceeded = 'Threshold Exceeded',
  CustomerPaused = 'Customer Paused',
  MembershipDataNotFound = 'No users in the source',
  DestinationGroupNotFound = 'Destination Group Not Found',
  NotOwnerOfDestinationGroup = 'Not Owner Of Destination Group',
  SecurityGroupNotFound = 'Security Group Not Found',
  PendingReview = 'Pending Review',
  SubmissionRejected = 'Submission Rejected',
}
