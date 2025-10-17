// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AxiosResponse } from "axios";

export interface ITitleApi {
  getTitle(prompt: string): Promise<AxiosResponse>;
};