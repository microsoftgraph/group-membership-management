// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type SpotCheckPartResult = {
  index: number;
  type: string;
  supported: boolean;
  exclusionary: boolean;
  included: boolean | null;
};

export type SpotCheckResult = {
  accountEnabled: boolean;
  hasUnsupportedParts: boolean;
  parts: SpotCheckPartResult[];
};
