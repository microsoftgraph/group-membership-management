// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type IFilterPart = {
    attribute: string;
    equalityOperator: string;
    value: string;
    andOr: string;
};
