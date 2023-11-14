// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type ODataQueryOptions = {
  $top?: number;
  $skip?: number;
  $filter?: string;
  $orderBy?: string;
};
