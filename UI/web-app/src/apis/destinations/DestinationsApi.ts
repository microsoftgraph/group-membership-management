// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AxiosResponse } from 'axios';
import { ApiBase } from '../ApiBase';
import { IDestinationsApi } from './IDestinationsApi';
import { PostGroupRequest } from '../../models/PostGroupRequest';
import { PostGroupResponse } from '../../models/PostGroupResponse';


export class DestinationsApi extends ApiBase implements IDestinationsApi {
  public async createGroup(postGroupRequest: PostGroupRequest): Promise<AxiosResponse<PostGroupResponse>> {
    const response = await this.httpClient.post<PostGroupResponse>('/groups', postGroupRequest, { timeout: 60_000 });
    this.ensureSuccessStatusCode(response);
    return response;
  }
};
