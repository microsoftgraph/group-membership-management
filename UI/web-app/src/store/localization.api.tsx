// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';

import { ThunkConfig } from './store';
import { IStrings, defaultLanguage } from '../services/localization';

export const setLanguage = createAsyncThunk<{ language: string; strings: IStrings }, string | undefined, ThunkConfig>(
  'localization/setLanguage',
  async (language, { extra }) => {
    const { localizationService } = extra;

    const newLanguage = language ?? defaultLanguage;
    localizationService.changeLanguage(newLanguage);

    const newStrings = localizationService.getStrings();

    return { language: newLanguage, strings: newStrings };
  }
);
