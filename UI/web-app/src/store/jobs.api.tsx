// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import moment from 'moment';
import { SyncStatus, ActionRequired } from '../models/Status';
import { loginRequest, config } from '../authConfig';
import { msalInstance } from '../index';
import { type Job } from '../models/Job';

export const fetchJobs = createAsyncThunk('jobs/fetchJobs', async () => {
  const account = msalInstance.getActiveAccount();
  if (account == null) {
    throw Error(
      'No active account! Verify a user has been signed in and setActiveAccount has been called.'
    );
  }

  const authResult = await msalInstance.acquireTokenSilent({
    ...loginRequest,
    account,
  });

  const headers = new Headers();
  const bearer = `Bearer ${authResult.accessToken}`;
  headers.append('Authorization', bearer);

  const options = {
    method: 'GET',
    headers,
  };

  try {
    const response = await fetch(config.getJobs, options).then(
      async (response) => await response.json()
    );

    const payload: Job[] = response;

    const mapped = payload.map((index) => {
      const currentTime = moment.utc();
      var lastRunTime = moment(index['lastSuccessfulRunTime']);
      var hoursAgo = currentTime.diff(lastRunTime, 'hours');
      index['lastSuccessfulRunTime'] =
        hoursAgo > index['period']
          ? moment
              .utc(index['lastSuccessfulRunTime'])
              .local()
              .format('MM/DD/YYYY') +
            ' >' +
            index['period'] +
            ' hrs ago'
          : moment
              .utc(index['lastSuccessfulRunTime'])
              .local()
              .format('MM/DD/YYYY') +
            ' ' +
            hoursAgo.toString() +
            ' hrs ago';

      var nextRunTime = moment(index['estimatedNextRunTime']);
      var hoursLeft = Math.abs(currentTime.diff(nextRunTime, 'hours'));
      index['estimatedNextRunTime'] =
        hoursAgo > index['period'] ||
        index['status'] === SyncStatus.CustomerPaused
          ? ''
          : moment
              .utc(index['estimatedNextRunTime'])
              .local()
              .format('MM/DD/YYYY') +
            ' ' +
            hoursLeft.toString() +
            ' hrs left';

      index['enabledOrNot'] =
        index['status'] === SyncStatus.CustomerPaused ? 'Disabled' : 'Enabled';

      index['arrow'] = '';

      return index;
    });

    const newPayload = mapped.map((index) => {
      switch (index['status']) {
        case SyncStatus.ThresholdExceeded:
          index['actionRequired'] = ActionRequired.ThresholdExceeded;
          break;
        case SyncStatus.CustomerPaused:
          index['actionRequired'] = ActionRequired.CustomerPaused;
          break;
        case SyncStatus.CustomMembershipDataNotFound:
          index['actionRequired'] = ActionRequired.CustomMembershipDataNotFound;
          break;
        case SyncStatus.DestinationGroupNotFound:
          index['actionRequired'] = ActionRequired.DestinationGroupNotFound;
          break;
        case SyncStatus.NotOwnerOfDestinationGroup:
          index['actionRequired'] = ActionRequired.NotOwnerOfDestinationGroup;
          break;
        case SyncStatus.SecurityGroupNotFound:
          index['actionRequired'] = ActionRequired.SecurityGroupNotFound;
          break;
      }
      return index;
    });

    return newPayload;

    return payload;
  } catch (error) {
    throw new Error('Failed to fetch jobs!');
  }
});
