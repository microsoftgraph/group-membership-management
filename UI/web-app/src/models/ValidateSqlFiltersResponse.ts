// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface ValidateSqlFiltersResponse {
    isValid: boolean;
    errors: Map<number, string>;
}