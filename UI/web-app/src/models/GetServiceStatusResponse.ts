// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ServiceStatuses } from './ServiceStatuses';

export interface GetServiceStatusResponse {
    statusCode: number;
    errorCode: string | null;
    status: ServiceStatuses;
  };

