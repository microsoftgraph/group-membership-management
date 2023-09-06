// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import moment from 'moment';
import { SyncStatus, ActionRequired } from '../models/Status';
import { loginRequest, config } from '../authConfig';
import { msalInstance } from '../index';
import { type Job } from '../models/Job';

export class OdataQueryOptions {
  pageSize?: number;
  itemsToSkip?: number;
  filter?: String;
  orderBy?: String;
}

export interface JobsResponse {
  jobs: Job[];
  totalNumberOfPages: number;
}

export const fetchJobs = createAsyncThunk('jobs/fetchJobs', async (odataQueryOptions?: OdataQueryOptions | undefined) => {
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

    let odataQuery: String = '';
    if (odataQueryOptions !== undefined) {
      if (odataQueryOptions.pageSize !== undefined) {
        odataQuery = "?$top=" + odataQueryOptions.pageSize;
      }

      if (odataQueryOptions.itemsToSkip !== undefined) {
        if (odataQuery !== '') {
          odataQuery += "&$skip=" + odataQueryOptions.itemsToSkip;
        } else {
          odataQuery = "?$skip=" + odataQueryOptions.itemsToSkip;
        }
      }

      if (odataQueryOptions.filter !== undefined) {
        if (odataQuery !== '') {
          odataQuery += "&$filter=" + odataQueryOptions.filter;
        } else {
          odataQuery = "?$filter=" + odataQueryOptions.filter;
        }
      }

      if (odataQueryOptions.orderBy !== undefined) {
        if (odataQuery !== '') {
          odataQuery += "&$orderby=" + odataQueryOptions.orderBy;
        } else {
          odataQuery = "?$orderby=" + odataQueryOptions.orderBy;
        }
      }
    }

    const response = await fetch(config.getJobs + odataQuery, options).then(
      async (response) => {
        let jobs = await response.json();
        let totalNumberOfPages  = response.headers.get('X-Total-Pages')?.toString() ?? '1';
        var jobsResponse: JobsResponse = { jobs: jobs, totalNumberOfPages: parseInt(totalNumberOfPages) };
        return jobsResponse;
      }
    );

    const payload: JobsResponse = response;

    const mapped = payload.jobs.map((index) => {
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
      var isPast = nextRunTime.isBefore(moment()); // Check if nextRunTime is in the past
      var hoursLeft = isPast ? 0 : Math.abs(currentTime.diff(nextRunTime, 'hours'));

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
        case SyncStatus.MembershipDataNotFound:
          index['actionRequired'] = ActionRequired.MembershipDataNotFound;
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

    payload.jobs = newPayload;
    return payload;
  } catch (error) {
    throw new Error('Failed to fetch jobs!');
  }
});
