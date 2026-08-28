// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface ResolveAttributeMappingsRequest {
  attribute: string;
  type: string | undefined;
  hasMapping: boolean | undefined;
  codes: string[];
};
