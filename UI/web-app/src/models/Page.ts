// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type Page<T> = {
  items: T[];
  totalNumberOfPages: number;
}