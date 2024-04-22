// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SqlMembershipAttributeValue } from "./SqlMembershipAttributeValue";

export interface GetAttributeValuesResponse {
    values: SqlMembershipAttributeValue[];
    attribute: string;
    type: string | undefined;
}