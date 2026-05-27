// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { format } from '@fluentui/react/lib/Utilities';
import { Job } from '../models/Job';
import { ActionRequired, RunHistoryStatus, SyncStatus } from '../models/Status';
import { formatLastRunTime, formatNextRunTime } from './dateUtils';
import { strings } from '../services/localization/i18n/locales/en/translations';

export const processJob = (job: Job): Job => {
  job['enabledOrNot'] = job['status'] === SyncStatus.Idle || job['status'] === SyncStatus.InProgress ? true : false;
  const lastRunTime = formatLastRunTime(job['lastSuccessfulRunTime']);
  const estimatedNextRunTime = formatNextRunTime(job['estimatedNextRunTime'], job['enabledOrNot']);
  const SQLMinDate = new Date(Date.UTC(1753, 0, 1));

  if (lastRunTime[0] === SQLMinDate.toLocaleDateString()) {
    // Jobs that haven't run yet
    job['lastSuccessfulRunTime'] = 'Pending initial sync';
    job['estimatedNextRunTime'] = 'Pending initial sync';
  } else {
    job['lastSuccessfulRunTime'] = `${format(strings.hoursAgo, lastRunTime[0], lastRunTime[1])}`;
    job['estimatedNextRunTime'] =
      estimatedNextRunTime[0] === '-'
        ? '-' // Disabled jobs
        : `${format(strings.hoursLeft, estimatedNextRunTime[0], estimatedNextRunTime[1])}`;
  }

  job['arrow'] = '';

  switch (job['status']) {
    case SyncStatus.ThresholdExceeded:
      job['actionRequired'] = ActionRequired.ThresholdExceeded;
      break;
    case SyncStatus.CustomerPaused:
      job['actionRequired'] = ActionRequired.Paused;
      break;
    case SyncStatus.DeveloperPaused:
      job['actionRequired'] = ActionRequired.DeveloperPaused;
      break;
    case SyncStatus.MembershipDataNotFound:
      job['actionRequired'] = ActionRequired.MembershipDataNotFound;
      break;
    case SyncStatus.DestinationGroupNotFound:
      job['actionRequired'] = ActionRequired.DestinationGroupNotFound;
      break;
    case SyncStatus.NotOwnerOfDestinationGroup:
      job['actionRequired'] = ActionRequired.NotOwnerOfDestinationGroup;
      break;
    case SyncStatus.SecurityGroupNotFound:
      job['actionRequired'] = ActionRequired.SecurityGroupNotFound;
      break;
    case SyncStatus.PendingReview:
      job['actionRequired'] = ActionRequired.PendingReview;
      break;
    case SyncStatus.PendingConfiguration:
      job['actionRequired'] = ActionRequired.PendingConfiguration;
      break;
    case SyncStatus.SubmissionRejected:
      job['actionRequired'] = ActionRequired.SubmissionRejected;
      break;
    case SyncStatus.NestedGroupsFound:
      job['actionRequired'] = ActionRequired.NestedGroupsFound;
      break;
    case SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup:
      job['actionRequired'] = ActionRequired.GuestUsersCannotBeAddedToUnifiedGroup;
      break;
  }

  return job;
};

// Get the display action required for a job based on user role
export const getDisplayActionRequired = (job: Job, canReviewJob: boolean): string => {
  if (job.status === SyncStatus.PendingConfiguration && !canReviewJob) {
    return ActionRequired.PendingReview;
  }
  return job.actionRequired;
};

export interface DebouncedFunction<T extends (...args: any[]) => void> {
  (...args: Parameters<T>): void;
  cancel: () => void;
  flush: () => void;
}

export function debounce<T extends (...args: any[]) => void>(func: T, wait: number): DebouncedFunction<T> {
  let timeout: ReturnType<typeof setTimeout> | undefined;
  let lastThis: ThisParameterType<T> | undefined;
  let lastArgs: Parameters<T> | undefined;

  const debounced = function (this: ThisParameterType<T>, ...args: Parameters<T>) {
    lastThis = this;
    lastArgs = args;
    if (timeout) clearTimeout(timeout);
    timeout = setTimeout(() => {
      timeout = undefined;
      const argsToUse = lastArgs;
      const thisToUse = lastThis;
      lastArgs = undefined;
      lastThis = undefined;
      if (!argsToUse) return;
      func.apply(thisToUse, argsToUse);
    }, wait);
  } as DebouncedFunction<T>;

  debounced.cancel = () => {
    if (timeout) {
      clearTimeout(timeout);
      timeout = undefined;
    }
    lastArgs = undefined;
    lastThis = undefined;
  };

  debounced.flush = () => {
    if (timeout) {
      clearTimeout(timeout);
      timeout = undefined;
      if (lastArgs) {
        const argsToUse = lastArgs;
        const thisToUse = lastThis;
        lastArgs = undefined;
        lastThis = undefined;
        func.apply(thisToUse, argsToUse);
      }
    }
  };

  return debounced;
};

// Get the display text for a sync job history status
export const getStatusDisplayText = (status: string): string => {
  const statusStrings = strings.JobDetails.Panel.RunHistoryStatus;
  switch (status) {
    case RunHistoryStatus.Idle:
      return statusStrings.idle;
    case RunHistoryStatus.Error:
    case RunHistoryStatus.ErroredDueToStuckInProgress:
    case RunHistoryStatus.QueryNotValid:
    case RunHistoryStatus.DestinationQueryNotValid:
    case RunHistoryStatus.FileNotFound:
    case RunHistoryStatus.FilePathNotValid:
    case RunHistoryStatus.SchemaError:
    case RunHistoryStatus.TransientError:
      return statusStrings.failed;
    case RunHistoryStatus.DestinationGroupNotFound:
      return statusStrings.destinationGroupNotFound;
    case RunHistoryStatus.SecurityGroupNotFound:
      return statusStrings.securityGroupNotFound;
    case RunHistoryStatus.MembershipDataNotFound:
      return statusStrings.membershipDataNotFound;
    case RunHistoryStatus.NotOwnerOfDestinationGroup:
      return statusStrings.notOwnerOfDestinationGroup;
    case RunHistoryStatus.GuestUsersCannotBeAddedToUnifiedGroup:
      return statusStrings.guestUsersNotSupported;
    case RunHistoryStatus.ThresholdExceeded:
      return statusStrings.thresholdExceeded;
    default:
      // Return unknown statuses as-is for runtime safety
      return status;
  }
};
