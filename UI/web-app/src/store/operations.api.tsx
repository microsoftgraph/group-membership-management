// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { OperationStatus } from '../models/OperationStatus';
import { ThunkConfig } from './store';

export const fetchOperationStatus = createAsyncThunk<OperationStatus, void, ThunkConfig>(
  'operations/fetchOperationStatus',
  async (_, { extra }) => {
    const { gmmApi } = extra.apis;

    try {
      return await gmmApi.operationsApi.fetchOperationStatus();
    } catch (error) {
      throw new Error('Failed to fetch settings data!');
    }
  }
);

export const stopOperation = createAsyncThunk<void, void, ThunkConfig>(
    'operations/stopOperation',
    async (_, { extra }) => {
      const { gmmApi } = extra.apis;
      try {
        await gmmApi.operationsApi.stopOperation();
      } catch (error) {
        throw new Error('Failed to stop the operation!');
      }
    }
  );
  
  export const resetOperation = createAsyncThunk<void, void, ThunkConfig>(
    'operations/resetOperation',
    async (_, { extra }) => {
      const { gmmApi } = extra.apis;
      try {
        await gmmApi.operationsApi.resetOperation();
      } catch (error) {
        throw new Error('Failed to reset the operation!');
      }
    }
  );