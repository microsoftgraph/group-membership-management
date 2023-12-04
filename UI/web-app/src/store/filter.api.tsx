// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { ThunkConfig } from './store';
import { PeoplePickerPersona } from '../models/PeoplePickerPersona';

export const getUsersForPeoplePicker = createAsyncThunk<PeoplePickerPersona[], {displayName: string; alias: string}, ThunkConfig>(
  'filter/getUsersForPeoplePicker',
  async (input, { extra }) => {
    const { graphApi } = extra.apis;
    try {
      return await graphApi.getUsersForPeoplePicker(input.displayName, input.alias);
    } catch (error) {
      throw new Error('Failed to call getUsersForPeoplePicker endpoint');
    }
  }
);
