// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SqlMembershipAttributeMapping } from './SqlMembershipAttributeMapping';

export interface ResolveAttributeMappingsResponse {
    mappings: SqlMembershipAttributeMapping[];
    attribute: string;
    type: string | undefined;
};
