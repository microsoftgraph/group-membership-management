// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { format } from '@fluentui/react/lib/Utilities';
import { Job } from '../models/Job';
import { ActionRequired, SyncStatus } from '../models/Status';
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
      job['actionRequired'] = ActionRequired.CustomerPaused;
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
    case SyncStatus.SubmissionRejected:
      job['actionRequired'] = ActionRequired.SubmissionRejected;
      break;
  }

  return job;
};

export function debounce<T extends (...args: any[]) => void>(func: T, wait: number) {
  let timeout: NodeJS.Timeout;
  return function (this: ThisParameterType<T>, ...args: Parameters<T>) {
    clearTimeout(timeout);
    timeout = setTimeout(() => func.apply(this, args), wait);
  };
}
