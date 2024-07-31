// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { ServiceStatuses } from '../models/ServiceStatuses';
import { ThunkConfig } from './store';
import { Operations } from '../models/Operations';

export const fetchServiceStatus = createAsyncThunk<ServiceStatuses, void, ThunkConfig>(
  'operations/fetchServiceStatus',
  async (_, { extra }) => {
    const { gmmApi } = extra.apis;

    try {
      return await gmmApi.operationsApi.fetchServiceStatus();
    } catch (error) {
      throw new Error('Failed to fetch service status data!');
    }
  }
);

export const processOperation = createAsyncThunk<void, Operations, ThunkConfig>(
  'operations/processOperation',
  async (operation, { extra }) => {
    const { gmmApi } = extra.apis;
    try {
      await gmmApi.operationsApi.processOperation(operation);
    } catch (error) {
      throw new Error(`Failed to process the ${operation} operation!`);
    }
  }
);

// Usage examples:
export const resetOperation = () => processOperation(Operations.Reset);
export const stopOperation = () => processOperation(Operations.Stop);
export const startOperation = () => processOperation(Operations.Start);