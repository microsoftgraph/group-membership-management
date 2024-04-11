// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ApiBase } from '../ApiBase';
import { IRolesApi, Roles } from './IRolesApi';

export class RolesApi extends ApiBase implements IRolesApi {
  public async getAllRoles(): Promise<Roles> {
    const response = await this.httpClient.get<Roles>(`/getAllRoles`);
    this.ensureSuccessStatusCode(response);
    return response.data;
  }
}
