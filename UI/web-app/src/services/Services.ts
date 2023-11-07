// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IAuthenticationService } from './auth';
import { ILocalizationService } from './localization';

export type Services = {
  authenticationService: IAuthenticationService;
  localizationService: ILocalizationService;
};