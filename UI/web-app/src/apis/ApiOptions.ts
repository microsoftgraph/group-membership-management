// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type ApiOptions = {
  baseUrl: string;
  getTokenAsync: () => Promise<string>;
}
