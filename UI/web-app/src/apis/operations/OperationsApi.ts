// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ServiceStatuses, Operations } from '../../models';
import { ApiBase } from '../ApiBase';
import { IOperationsApi } from './IOperationsApi';


export class OperationsApi extends ApiBase implements IOperationsApi {

  public async processOperation(operation: Operations): Promise<void> {
    const response = await this.httpClient.post(`/operations/${operation}`, {});
    this.ensureSuccessStatusCode(response);
    return response.data;
  }

  public async fetchServiceStatus(): Promise<ServiceStatuses> {
    const response = await this.httpClient.get<ServiceStatuses>('/operations/servicestatus');
    this.ensureSuccessStatusCode(response);
    return response.data;
  }

}
