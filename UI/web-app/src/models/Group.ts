// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IFilterPart } from "./IFilterPart";

export interface Group {
  name: string;
  items: IFilterPart[];
  children: Group[];
  andOr: string;
}