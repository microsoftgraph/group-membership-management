// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import operationsReducer, {
  resetError,
  updateServiceStatus,
} from './operations.slice';
import { fetchServiceStatus, processOperation } from './operations.api';
import { ServiceStatuses } from '../models/ServiceStatuses';
import { Operations } from '../models/Operations';

const initial = operationsReducer(undefined, { type: '@@INIT' });

describe('operations.slice — reducers', () => {
  it('resetError clears error', () => {
    const seeded = { ...initial, error: 'some error' };
    expect(operationsReducer(seeded, resetError()).error).toBeNull();
  });

  it('updateServiceStatus sets status and displayStatus', () => {
    const state = operationsReducer(initial, updateServiceStatus(ServiceStatuses.Running));
    expect(state.status).toBe(ServiceStatuses.Running);
    expect(state.displayStatus).toBe(ServiceStatuses.Running);
  });
});

describe('operations.slice — fetchServiceStatus extraReducers', () => {
  it('pending sets loading', () => {
    const state = operationsReducer(initial, fetchServiceStatus.pending('req1', undefined as any));
    expect(state.isLoading).toBe(true);
    expect(state.error).toBeNull();
  });

  it('fulfilled sets status', () => {
    const state = operationsReducer(initial, fetchServiceStatus.fulfilled(ServiceStatuses.Running, 'req1', undefined as any));
    expect(state.isLoading).toBe(false);
    expect(state.status).toBe(ServiceStatuses.Running);
    expect(state.displayStatus).toBe(ServiceStatuses.Running);
  });

  it('fulfilled preserves Resetting displayStatus when status is Stopped', () => {
    const seeded = { ...initial, displayStatus: ServiceStatuses.Resetting };
    const state = operationsReducer(seeded, fetchServiceStatus.fulfilled(ServiceStatuses.Stopped, 'req1', undefined as any));
    expect(state.status).toBe(ServiceStatuses.Stopped);
    expect(state.displayStatus).toBe(ServiceStatuses.Resetting);
  });

  it('rejected sets error', () => {
    const state = operationsReducer(initial, fetchServiceStatus.rejected(new Error('e'), 'req1', undefined as any));
    expect(state.isLoading).toBe(false);
    expect(state.error).toBe('Failed to fetch service status.');
  });
});

describe('operations.slice — processOperation extraReducers', () => {
  it('pending Stop sets Stopping status', () => {
    const state = operationsReducer(initial, processOperation.pending('req1', Operations.Stop));
    expect(state.isLoading).toBe(true);
    expect(state.isOperationInProgress).toBe(true);
    expect(state.status).toBe(ServiceStatuses.Stopping);
    expect(state.displayStatus).toBe(ServiceStatuses.Stopping);
  });

  it('pending Reset sets Resetting status', () => {
    const state = operationsReducer(initial, processOperation.pending('req1', Operations.Reset));
    expect(state.status).toBe(ServiceStatuses.Resetting);
  });

  it('pending Start sets Starting status', () => {
    const state = operationsReducer(initial, processOperation.pending('req1', Operations.Start));
    expect(state.status).toBe(ServiceStatuses.Starting);
  });

  it('fulfilled clears loading', () => {
    const seeded = { ...initial, isLoading: true };
    const state = operationsReducer(seeded, processOperation.fulfilled(undefined as any, 'req1', Operations.Stop));
    expect(state.isLoading).toBe(false);
  });

  it('rejected sets error with operation name', () => {
    const state = operationsReducer(initial, processOperation.rejected(new Error('e'), 'req1', Operations.Stop));
    expect(state.isLoading).toBe(false);
    expect(state.error).toBeTruthy();
    expect(state.isOperationInProgress).toBe(false);
  });
});
