// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SqlMembershipAttribute, SqlMembershipSource } from '../../models';

export interface ISqlMembershipSourcesApi {
  fetchDefaultSqlMembershipSource(): Promise<SqlMembershipSource>;
  fetchDefaultSqlMembershipSourceAttributes(): Promise<SqlMembershipAttribute[]>;
  patchDefaultSqlMembershipSourceCustomLabel(customLabel: string): Promise<void>;
  patchDefaultSqlMembershipSourceAttributes(attributes: SqlMembershipAttribute[]): Promise<void>;
}
