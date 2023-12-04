// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { User } from '../models/User';
import { PeoplePickerPersona } from '../models/PeoplePickerPersona';

export interface IGraphApi {
  getPreferredLanguage(user: User): Promise<string>;
  getProfilePhotoUrl(user: User): Promise<string>;
  getUsersForPeoplePicker(displayName: string, mail: string): Promise<PeoplePickerPersona[]>;
}