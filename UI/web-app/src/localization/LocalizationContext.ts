// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createContext } from 'react';
import { IStrings } from './IStrings';

export type ILocalizationContext = {
  setUserPreferredLanguage: (userPreferredLanguage: string | undefined) => void;
  strings: IStrings;
};

// Create a context with a default value
export const LocalizationContext  = createContext<ILocalizationContext>({
  setUserPreferredLanguage: () => {},
  strings: {} as IStrings
});
