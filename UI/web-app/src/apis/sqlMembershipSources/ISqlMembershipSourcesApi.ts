// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SqlMembershipAttribute, SqlMembershipSource, SqlMembershipAttributeMapping } from '../../models';

export interface ISqlMembershipSourcesApi {
  fetchDefaultSqlMembershipSource(): Promise<SqlMembershipSource>;
  fetchDefaultSqlMembershipSourceAttributes(): Promise<SqlMembershipAttribute[]>;
  fetchDefaultSqlMembershipSourceAttributeMappings(attribute: string): Promise<SqlMembershipAttributeMapping[]>;
  patchDefaultSqlMembershipSourceCustomLabel(customLabel: string): Promise<void>;
  patchDefaultSqlMembershipSourceAttributes(attributes: SqlMembershipAttribute[]): Promise<void>;
}
