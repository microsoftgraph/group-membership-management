// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IStrings } from './IStrings';

export interface ILocalizationService {
  
  /** Changes the language used for translations. */
  changeLanguage: (language: string) => void;

  /** Gets the strings for the current language. */
  getStrings: () => IStrings;

};
