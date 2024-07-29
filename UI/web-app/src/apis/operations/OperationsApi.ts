// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { OperationStatus } from '../../models/OperationStatus';
import { ApiBase } from '../ApiBase';
import { IOperationsApi } from './IOperationsApi';


export class OperationsApi extends ApiBase implements IOperationsApi {
  public async fetchOperationStatus(): Promise<OperationStatus> {
    const response = await this.httpClient.get<OperationStatus>('/');
    this.ensureSuccessStatusCode(response);
    return response.data;
  }
  public async stopOperation(): Promise<void> {
    const response = await this.httpClient.post('/stop', {});
    this.ensureSuccessStatusCode(response);
    return response.data;  
  }
  public async resetOperation(): Promise<void> {
    const response = await this.httpClient.post('/reset', {});
    this.ensureSuccessStatusCode(response);
    return response.data; 
  }

}
