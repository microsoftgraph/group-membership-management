// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SqlMembershipAttribute, SqlMembershipSource, SqlMembershipAttributeValue } from '../../models';

export interface ISqlMembershipSourcesApi {
  fetchDefaultSqlMembershipSource(): Promise<SqlMembershipSource>;
  fetchDefaultSqlMembershipSourceAttributes(): Promise<SqlMembershipAttribute[]>;
  fetchDefaultSqlMembershipSourceAttributeValues(attribute: string): Promise<SqlMembershipAttributeValue[]>;
  patchDefaultSqlMembershipSourceCustomLabel(customLabel: string): Promise<void>;
  patchDefaultSqlMembershipSourceAttributes(attributes: SqlMembershipAttribute[]): Promise<void>;
}
