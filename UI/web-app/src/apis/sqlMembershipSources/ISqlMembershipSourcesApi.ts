// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SqlMembershipAttribute, SqlMembershipSource, SqlMembershipAttributeMapping } from '../../models';
import { ValidateSqlFiltersResponse } from '../../models/ValidateSqlFiltersResponse';

export interface AttributeMappingsPage {
  mappings: SqlMembershipAttributeMapping[];
  hasMore: boolean;
};

export interface ISqlMembershipSourcesApi {
  fetchDefaultSqlMembershipSource(): Promise<SqlMembershipSource>;
  fetchDefaultSqlMembershipSourceAttributes(): Promise<SqlMembershipAttribute[]>;
  fetchDefaultSqlMembershipSourceAttributeMappings(attribute: string, search?: string, top?: number): Promise<AttributeMappingsPage>;
  resolveDefaultSqlMembershipSourceAttributeMappings(attribute: string, codes: string[]): Promise<SqlMembershipAttributeMapping[]>;
  fetchDefaultSqlMembershipSourceAttributeValues(attribute: SqlMembershipAttribute): Promise<string[]>;
  patchDefaultSqlMembershipSourceCustomLabel(customLabel: string): Promise<void>;
  patchDefaultSqlMembershipSourceAttributes(attributes: SqlMembershipAttribute[]): Promise<void>;
  validateSqlFilters(filters: Map<number, string>): Promise<ValidateSqlFiltersResponse>;
};
