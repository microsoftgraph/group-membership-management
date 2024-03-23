// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type IFilterPart = {
    id: number;
    attribute: string;
    equalityOperator: string;
    attributeValue: string;
    orAndOperator: string;
};
