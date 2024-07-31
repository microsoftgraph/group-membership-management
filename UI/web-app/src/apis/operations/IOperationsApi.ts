// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ServiceStatuses, Operations } from '../../models';
export interface IOperationsApi {
  fetchServiceStatus(): Promise<ServiceStatuses>;
  processOperation(operation: Operations): Promise<void>;
}