// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it, vi } from 'vitest';
import { processJob, getDisplayActionRequired, getStatusDisplayText, debounce } from './jobUtils';
import { ActionRequired, SyncStatus, RunHistoryStatus } from '../models/Status';
import type { Job } from '../models/Job';

const makeJob = (status: string, overrides: Partial<Job> = {}): Job =>
  ({
    syncJobId: 'test-id',
    targetGroupId: 'group-id',
    status,
    lastSuccessfulRunTime: '2025-01-15T10:00:00Z',
    estimatedNextRunTime: '2025-01-16T10:00:00Z',
    titles: [],
    ...overrides,
  } as unknown as Job);

describe('processJob', () => {
  it('sets enabledOrNot to true for Idle status', () => {
    const job = processJob(makeJob(SyncStatus.Idle));
    expect(job.enabledOrNot).toBe(true);
  });

  it('sets enabledOrNot to true for InProgress status', () => {
    const job = processJob(makeJob(SyncStatus.InProgress));
    expect(job.enabledOrNot).toBe(true);
  });

  it('sets enabledOrNot to false for other statuses', () => {
    const job = processJob(makeJob(SyncStatus.CustomerPaused));
    expect(job.enabledOrNot).toBe(false);
  });

  it('sets Pending initial sync for SQL min date', () => {
    const sqlMinDate = new Date(Date.UTC(1753, 0, 1)).toISOString();
    const job = processJob(makeJob(SyncStatus.Idle, {
      lastSuccessfulRunTime: sqlMinDate,
      estimatedNextRunTime: sqlMinDate,
    }));
    expect(job.lastSuccessfulRunTime).toBe('Pending initial sync');
    expect(job.estimatedNextRunTime).toBe('Pending initial sync');
  });

  it('sets actionRequired for ThresholdExceeded', () => {
    const job = processJob(makeJob(SyncStatus.ThresholdExceeded));
    expect(job.actionRequired).toBe(ActionRequired.ThresholdExceeded);
  });

  it('sets actionRequired for CustomerPaused', () => {
    const job = processJob(makeJob(SyncStatus.CustomerPaused));
    expect(job.actionRequired).toBe(ActionRequired.Paused);
  });

  it('sets actionRequired for DeveloperPaused', () => {
    const job = processJob(makeJob(SyncStatus.DeveloperPaused));
    expect(job.actionRequired).toBe(ActionRequired.DeveloperPaused);
  });

  it('sets actionRequired for MembershipDataNotFound', () => {
    const job = processJob(makeJob(SyncStatus.MembershipDataNotFound));
    expect(job.actionRequired).toBe(ActionRequired.MembershipDataNotFound);
  });

  it('sets actionRequired for DestinationGroupNotFound', () => {
    const job = processJob(makeJob(SyncStatus.DestinationGroupNotFound));
    expect(job.actionRequired).toBe(ActionRequired.DestinationGroupNotFound);
  });

  it('sets actionRequired for NotOwnerOfDestinationGroup', () => {
    const job = processJob(makeJob(SyncStatus.NotOwnerOfDestinationGroup));
    expect(job.actionRequired).toBe(ActionRequired.NotOwnerOfDestinationGroup);
  });

  it('sets actionRequired for SecurityGroupNotFound', () => {
    const job = processJob(makeJob(SyncStatus.SecurityGroupNotFound));
    expect(job.actionRequired).toBe(ActionRequired.SecurityGroupNotFound);
  });

  it('sets actionRequired for PendingReview', () => {
    const job = processJob(makeJob(SyncStatus.PendingReview));
    expect(job.actionRequired).toBe(ActionRequired.PendingReview);
  });

  it('sets actionRequired for PendingConfiguration', () => {
    const job = processJob(makeJob(SyncStatus.PendingConfiguration));
    expect(job.actionRequired).toBe(ActionRequired.PendingConfiguration);
  });

  it('sets actionRequired for SubmissionRejected', () => {
    const job = processJob(makeJob(SyncStatus.SubmissionRejected));
    expect(job.actionRequired).toBe(ActionRequired.SubmissionRejected);
  });

  it('sets actionRequired for NestedGroupsFound', () => {
    const job = processJob(makeJob(SyncStatus.NestedGroupsFound));
    expect(job.actionRequired).toBe(ActionRequired.NestedGroupsFound);
  });

  it('sets actionRequired for GuestUsersCannotBeAddedToUnifiedGroup', () => {
    const job = processJob(makeJob(SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup));
    expect(job.actionRequired).toBe(ActionRequired.GuestUsersCannotBeAddedToUnifiedGroup);
  });

  it('sets arrow to empty string', () => {
    const job = processJob(makeJob(SyncStatus.Idle));
    expect(job.arrow).toBe('');
  });

  it('formats estimatedNextRunTime as dash for disabled jobs', () => {
    const job = processJob(makeJob(SyncStatus.CustomerPaused));
    expect(job.estimatedNextRunTime).toBe('-');
  });
});

describe('getDisplayActionRequired', () => {
  it('returns PendingReview for PendingConfiguration when user cannot review', () => {
    const job = makeJob(SyncStatus.PendingConfiguration);
    job.actionRequired = ActionRequired.PendingConfiguration;
    expect(getDisplayActionRequired(job, false)).toBe(ActionRequired.PendingReview);
  });

  it('returns actual actionRequired for PendingConfiguration when user can review', () => {
    const job = makeJob(SyncStatus.PendingConfiguration);
    job.actionRequired = ActionRequired.PendingConfiguration;
    expect(getDisplayActionRequired(job, true)).toBe(ActionRequired.PendingConfiguration);
  });

  it('returns actual actionRequired for non-PendingConfiguration statuses', () => {
    const job = makeJob(SyncStatus.Idle);
    job.actionRequired = 'SomeAction';
    expect(getDisplayActionRequired(job, false)).toBe('SomeAction');
  });
});

describe('getStatusDisplayText', () => {
  it('returns idle text for Idle status', () => {
    const result = getStatusDisplayText(RunHistoryStatus.Idle);
    expect(result).toBeDefined();
    expect(typeof result).toBe('string');
  });

  it('returns failed text for Error status', () => {
    const result = getStatusDisplayText(RunHistoryStatus.Error);
    expect(result).toBeDefined();
  });

  it('returns failed text for ErroredDueToStuckInProgress', () => {
    const result = getStatusDisplayText(RunHistoryStatus.ErroredDueToStuckInProgress);
    expect(result).toBe(getStatusDisplayText(RunHistoryStatus.Error));
  });

  it('returns failed text for QueryNotValid', () => {
    const result = getStatusDisplayText(RunHistoryStatus.QueryNotValid);
    expect(result).toBe(getStatusDisplayText(RunHistoryStatus.Error));
  });

  it('returns failed text for TransientError', () => {
    const result = getStatusDisplayText(RunHistoryStatus.TransientError);
    expect(result).toBe(getStatusDisplayText(RunHistoryStatus.Error));
  });

  it('returns specific text for DestinationGroupNotFound', () => {
    const result = getStatusDisplayText(RunHistoryStatus.DestinationGroupNotFound);
    expect(result).toBeDefined();
  });

  it('returns specific text for SecurityGroupNotFound', () => {
    const result = getStatusDisplayText(RunHistoryStatus.SecurityGroupNotFound);
    expect(result).toBeDefined();
  });

  it('returns specific text for MembershipDataNotFound', () => {
    const result = getStatusDisplayText(RunHistoryStatus.MembershipDataNotFound);
    expect(result).toBeDefined();
  });

  it('returns specific text for NotOwnerOfDestinationGroup', () => {
    const result = getStatusDisplayText(RunHistoryStatus.NotOwnerOfDestinationGroup);
    expect(result).toBeDefined();
  });

  it('returns specific text for GuestUsersCannotBeAddedToUnifiedGroup', () => {
    const result = getStatusDisplayText(RunHistoryStatus.GuestUsersCannotBeAddedToUnifiedGroup);
    expect(result).toBeDefined();
  });

  it('returns specific text for ThresholdExceeded', () => {
    const result = getStatusDisplayText(RunHistoryStatus.ThresholdExceeded);
    expect(result).toBeDefined();
  });

  it('returns status as-is for unknown status', () => {
    const result = getStatusDisplayText('UnknownStatus');
    expect(result).toBe('UnknownStatus');
  });
});

describe('debounce', () => {
  it('collapses rapid calls into a single trailing-edge invocation with the latest args', () => {
    vi.useFakeTimers();
    try {
      const spy = vi.fn();
      const debounced = debounce(spy, 350);

      debounced('f');
      debounced('fa');
      debounced('fal');
      debounced('fallback');

      expect(spy).not.toHaveBeenCalled();

      vi.advanceTimersByTime(349);
      expect(spy).not.toHaveBeenCalled();

      vi.advanceTimersByTime(1);
      expect(spy).toHaveBeenCalledTimes(1);
      expect(spy).toHaveBeenCalledWith('fallback');
    } finally {
      vi.useRealTimers();
    }
  });

  it('cancel() prevents a pending invocation from firing', () => {
    vi.useFakeTimers();
    try {
      const spy = vi.fn();
      const debounced = debounce(spy, 350);

      debounced('pending');
      debounced.cancel();

      vi.advanceTimersByTime(1000);
      expect(spy).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });
});
