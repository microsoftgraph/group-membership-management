// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { User } from '../models/User';

export interface IGraphApi {
  getPreferredLanguage(user: User): Promise<string>;
  getProfilePhotoUrl(user: User): Promise<string>;
}