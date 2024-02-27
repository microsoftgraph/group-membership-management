// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SqlMembershipAttribute, SqlMembershipSource } from '../../models';
import { ApiBase } from '../ApiBase';
import { ISqlMembershipSourcesApi } from './ISqlMembershipSourcesApi';


export class SqlMembershipSourcesApi extends ApiBase implements ISqlMembershipSourcesApi {

  public async fetchDefaultSqlMembershipSource(): Promise<SqlMembershipAttribute> {
    const response = await this.httpClient.get<SqlMembershipSource>('/default');
    this.ensureSuccessStatusCode(response);
    return response.data;
  }

  public async fetchDefaultSqlMembershipSourceAttributes(): Promise<SqlMembershipAttribute[]> {
    const response = await this.httpClient.get<SqlMembershipAttribute[]>('/defaultAttributes');
    this.ensureSuccessStatusCode(response);
    return response.data;
  }

  public async patchDefaultSqlMembershipSourceCustomLabel(customLabel: string): Promise<void> {
    const response = await this.httpClient.patch<void>(
      '/default', 
      customLabel,
      { headers: { 'Content-Type': 'application/json' } }
    );
    this.ensureSuccessStatusCode(response);
  }

  public async patchDefaultSqlMembershipSourceAttributes(attributes: SqlMembershipAttribute[]): Promise<void> {
    const response = await this.httpClient.patch<void>('/defaultAttributes', attributes);
    this.ensureSuccessStatusCode(response);
  }

}
