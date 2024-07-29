// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { OperationStatus } from '../../models';

export interface IOperationsApi {
  fetchOperationStatus(): Promise<OperationStatus>;
  stopOperation(): Promise<void>;
  resetOperation(): Promise<void>;
}
