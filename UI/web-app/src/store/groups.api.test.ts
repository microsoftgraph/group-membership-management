// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Axios, AxiosError, AxiosHeaders, AxiosResponse } from 'axios';
import { DestinationsApi } from '../apis/destinations/DestinationsApi';
import { NewGroupType } from '../models/NewGroupType';
import { PostGroupResponse } from '../models/PostGroupResponse';
import { setupStore } from './store';
import { createGroup } from './groups.api';
import { resetCreateGroup } from './manageMembership.slice';

const request = { groupName: 'Synthetic name', groupAlias: 'SyntheticAlias', groupType: NewGroupType.Microsoft365, description: '' };
const response = (data: PostGroupResponse, status = 200): AxiosResponse<PostGroupResponse> => ({
  data, status, statusText: '', headers: {}, config: { headers: new AxiosHeaders() },
});

afterEach(() => vi.restoreAllMocks());

describe('createGroup', () => {
  it('uses the existing POST with a one-minute request timeout', async () => {
    const post = vi.spyOn(Axios.prototype, 'post')
      .mockResolvedValue(response({ ok: true, statusCode: 200, groupId: 'created-id' }));
    const api = new DestinationsApi({ baseUrl: 'https://example.test/destinations', getTokenAsync: async () => 'test-token' });
    await api.createGroup(request);
    expect(post).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith('/groups', request, { timeout: 60_000 });
  });

  it('allowlists the request and selects the returned group', async () => {
    const api = vi.spyOn(DestinationsApi.prototype, 'createGroup')
      .mockResolvedValue(response({ ok: true, statusCode: 200, groupId: 'created-id' }));
    const store = setupStore();
    const draft = { ...request, sensitivityLabel: 'private-example', policy: 'owner' };
    await store.dispatch(createGroup(draft));
    expect(api).toHaveBeenCalledWith(request);
    expect(store.getState().manageMembership.createdGroupId).toBe('created-id');
  });

  it('blocks synchronous duplicate submissions and another create after a known ID', async () => {
    let finish!: (value: AxiosResponse<PostGroupResponse>) => void;
    const api = vi.spyOn(DestinationsApi.prototype, 'createGroup')
      .mockReturnValue(new Promise(resolve => { finish = resolve; }));
    const store = setupStore();
    const first = store.dispatch(createGroup(request));
    await store.dispatch(createGroup(request));
    expect(api).toHaveBeenCalledTimes(1);
    finish(response({ ok: true, statusCode: 200, groupId: 'created-id' }));
    await first;
    await store.dispatch(createGroup(request));
    expect(api).toHaveBeenCalledTimes(1);
  });

  it.each([400, 401, 403, 404, 405, 409, 422])('retains a confirmed %s rejection and permits deliberate retry', async status => {
    const api = vi.spyOn(DestinationsApi.prototype, 'createGroup')
      .mockRejectedValue(new AxiosError('raw exception', 'ERR_BAD_REQUEST', undefined, undefined, response({ ok: false, statusCode: status, responseData: 'raw details' }, status)));
    const store = setupStore();
    const action = await store.dispatch(createGroup(request));
    expect(createGroup.rejected.match(action) && action.payload).toEqual({ kind: 'rejected', statusCode: status });
    expect(store.getState().manageMembership.createGroupLoading).toBe(false);
    expect(store.getState().manageMembership.createGroupUncertain).toBe(false);
    expect(store.getState().manageMembership.createGroupErrorMessage).not.toContain('raw');
    await store.dispatch(createGroup(request));
    expect(api).toHaveBeenCalledTimes(2);
  });

  it.each(['network', 'timeout', 'request-timeout', 'throttled', 'server', 'missing-id'] as const)('blocks retry for an uncertain %s result until a fresh flow', async outcome => {
    const api = vi.spyOn(DestinationsApi.prototype, 'createGroup');
    if (outcome === 'network') api.mockRejectedValue(new Error('lost response'));
    if (outcome === 'timeout') api.mockRejectedValue(new AxiosError('timeout of 60000ms exceeded', 'ECONNABORTED'));
    if (outcome === 'request-timeout') api.mockRejectedValue(new AxiosError('request timed out', 'ERR_BAD_RESPONSE', undefined, undefined, response({ ok: false, statusCode: 408 }, 408)));
    if (outcome === 'throttled') api.mockRejectedValue(new AxiosError('throttled', 'ERR_BAD_RESPONSE', undefined, undefined, response({ ok: false, statusCode: 429 }, 429)));
    if (outcome === 'server') api.mockRejectedValue(new AxiosError('server error', 'ERR_BAD_RESPONSE', undefined, undefined, response({ ok: false, statusCode: 500 }, 500)));
    if (outcome === 'missing-id') api.mockResolvedValue(response({ ok: true, statusCode: 200 }));
    const store = setupStore();
    await store.dispatch(createGroup(request));
    expect(store.getState().manageMembership.createGroupUncertain).toBe(true);
    expect(store.getState().manageMembership.createGroupLoading).toBe(false);
    await store.dispatch(createGroup({ ...request, groupName: 'Edited' }));
    expect(api).toHaveBeenCalledTimes(1);
    store.dispatch(resetCreateGroup());
    await store.dispatch(createGroup(request));
    expect(api).toHaveBeenCalledTimes(2);
  });
});
