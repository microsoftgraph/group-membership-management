// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SqlMembershipAttribute, SqlMembershipAttributeMapping, SqlMembershipSource } from '../../models';
import { ValidateSqlFiltersResponse } from '../../models/ValidateSqlFiltersResponse';
import { ApiBase } from '../ApiBase';
import { ISqlMembershipSourcesApi, AttributeMappingsPage } from './ISqlMembershipSourcesApi';


export class SqlMembershipSourcesApi extends ApiBase implements ISqlMembershipSourcesApi {

  public async fetchDefaultSqlMembershipSource(): Promise<SqlMembershipSource> {
    const response = await this.httpClient.get<SqlMembershipSource>('/default');
    this.ensureSuccessStatusCode(response);
    return response.data;
  }

  public async fetchDefaultSqlMembershipSourceAttributes(): Promise<SqlMembershipAttribute[]> {
    const response = await this.httpClient.get<SqlMembershipAttribute[]>('/defaultAttributes');
    this.ensureSuccessStatusCode(response);
    return response.data;
  }

  public async fetchDefaultSqlMembershipSourceAttributeMappings(attribute: string, search?: string, top?: number): Promise<AttributeMappingsPage> {
    const params: { search?: string; top?: number } = {};
    if (search) {
      params.search = search;
    }
    if (top) {
      params.top = top;
    }

    const response = await this.httpClient.get<AttributeMappingsPage>('/attributeMappings/' + attribute, { params });
    this.ensureSuccessStatusCode(response);
    return { mappings: response.data?.mappings ?? [], hasMore: response.data?.hasMore ?? false };
  }

  public async resolveDefaultSqlMembershipSourceAttributeMappings(attribute: string, codes: string[]): Promise<SqlMembershipAttributeMapping[]> {
    const response = await this.httpClient.post<SqlMembershipAttributeMapping[]>('/attributeMappings/' + attribute + '/resolve', codes);
    this.ensureSuccessStatusCode(response);
    return response.data ?? [];
  }

  public async fetchDefaultSqlMembershipSourceAttributeValues(attribute: SqlMembershipAttribute): Promise<string[]> {
    const response = await this.httpClient.get<string[]>('/attributeValues/' + attribute.name, { params: { hasMapping: attribute.hasMapping } });
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

  public async validateSqlFilters(filters: Map<number, string>): Promise<ValidateSqlFiltersResponse> {
    const response = await this.httpClient.post<ValidateSqlFiltersResponse>('/validateFilters', Object.fromEntries(filters));
    this.ensureSuccessStatusCode(response);
    return response.data;
  }

};
