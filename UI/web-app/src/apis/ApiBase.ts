// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import axios, { Axios, AxiosResponse } from 'axios';
import { ApiOptions } from './ApiOptions';

export abstract class ApiBase {
  protected httpClient: Axios;

  constructor(options: ApiOptions) {
    if (!options) throw new Error(`ApiOptions is required.`);
    if (!options.baseUrl) throw new Error(`options.baseUrl is required.`);
    if (!options.getTokenAsync) throw new Error(`options.getTokenAsync is required.`);

    this.httpClient = axios.create({
      baseURL: options.baseUrl,
    });

    this.httpClient.interceptors.request.use(async (config) => {
      const token = await options.getTokenAsync();
      config.headers.Authorization = `Bearer ${token}`;
      return config;
    });
  }

  protected ensureSuccessStatusCode(response: AxiosResponse) {
    if (response.status >= 400) {
      throw new Error(`Request failed with status code ${response.status}.`);
    }
  }
}
