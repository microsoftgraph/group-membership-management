// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createContext } from 'react';

export type LocalizationContext = {
  userPreferredLanguage: string | undefined;
};

// Create a context with a default value
export const DefaultLocalizationContext = createContext<LocalizationContext>({
  userPreferredLanguage: 'en',
});
