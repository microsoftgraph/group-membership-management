// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface GetAttributeMappingsRequest {
  attribute: string;
  type: string | undefined;
  hasMapping: boolean | undefined;
}