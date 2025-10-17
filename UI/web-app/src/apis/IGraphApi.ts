// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { User } from '../models/User';
import { PeoplePickerPersona } from '../models/PeoplePickerPersona';

export interface IGraphApi {
  getUser(input: string): Promise<string>;
  getPreferredLanguage(user: User): Promise<string>;
  getProfilePhotoUrl(user: User): Promise<string>;
  getPeoplePickerSuggestions(input: string): Promise<PeoplePickerPersona[]>;
  getProfilePhotoUrlUsingUserId(userId: string): Promise<string>;
};