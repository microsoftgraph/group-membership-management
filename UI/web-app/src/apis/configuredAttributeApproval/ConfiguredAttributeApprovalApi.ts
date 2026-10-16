// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { isAxiosError } from 'axios';
import { ApiBase } from '../ApiBase';
import {
  ConfiguredAttributeApprovalConfiguration,
  ConfiguredAttributeApprovalError,
  IConfiguredAttributeApprovalApi,
} from './IConfiguredAttributeApprovalApi';

export const toConfiguredAttributeApprovalError = (error: unknown): ConfiguredAttributeApprovalError => {
  if (isAxiosError(error)) {
    const data = error.response?.data as { message?: string; Message?: string } | undefined;
    return {
      status: error.response?.status ?? 0,
      message: data?.message ?? data?.Message ?? '',
    };
  }
  return { status: 0, message: error instanceof Error ? error.message : '' };
};

export class ConfiguredAttributeApprovalApi extends ApiBase implements IConfiguredAttributeApprovalApi {

  public async fetchConfiguration(): Promise<ConfiguredAttributeApprovalConfiguration> {
    const response = await this.httpClient.get<ConfiguredAttributeApprovalConfiguration>('/configuration');
    this.ensureSuccessStatusCode(response);
    return response.data;
  }

  public async saveConfiguration(configuration: ConfiguredAttributeApprovalConfiguration): Promise<ConfiguredAttributeApprovalConfiguration> {
    const response = await this.httpClient.put<ConfiguredAttributeApprovalConfiguration>('/configuration', configuration);
    this.ensureSuccessStatusCode(response);
    return response.data;
  }
}
