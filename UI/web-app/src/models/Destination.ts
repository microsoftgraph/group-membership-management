// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface Destination {
    id: string;
    name: string;
    type: string;
    endpoints?: string[] | undefined;
    email?: string;
  }
  