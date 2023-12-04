// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ApiBase } from '../ApiBase';
import { IRolesApi } from './IRolesApi';

export class RolesApi extends ApiBase implements IRolesApi {
  public async getIsAdmin(): Promise<boolean> {
    const response = await this.httpClient.get<boolean>(`/isAdmin`);
    this.ensureSuccessStatusCode(response);
    return response.data;
  }
}
