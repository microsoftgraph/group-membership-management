// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SqlMembershipAttributeMapping } from "./SqlMembershipAttributeMapping";

export interface GetAttributeMappingsResponse {
    mappings: SqlMembershipAttributeMapping[];
    attribute: string;
    type: string | undefined;
}