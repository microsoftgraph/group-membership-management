// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type ODataQueryOptions = {
  [key: string]: string | number | undefined;
  top?: number;
  skip?: number;
  filter?: string;
  orderBy?: string;
};
