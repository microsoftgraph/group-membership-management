// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AxiosResponse } from 'axios';
import { ApiBase } from '../ApiBase';
import { ITitleApi } from './ITitleApi';


export class TitleApi extends ApiBase implements ITitleApi {

  public async getTitle(filter: string): Promise<AxiosResponse> {
    const response = await this.httpClient.post(`/OpenAI/generateTitle`, { filter });
    this.ensureSuccessStatusCode(response);
    return response;
  }
}