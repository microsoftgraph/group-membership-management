// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AxiosResponse } from 'axios';
import { ApiBase } from '../ApiBase';
import { IDestinationsApi } from './IDestinationsApi';
import { PostGroupRequest } from '../../models/PostGroupRequest';


export class DestinationsApi extends ApiBase implements IDestinationsApi {
  public async createGroup(postGroupRequest: PostGroupRequest): Promise<AxiosResponse> {
    const response = await this.httpClient.post('/groups', postGroupRequest);
    this.ensureSuccessStatusCode(response);
    return response;
  }
};
