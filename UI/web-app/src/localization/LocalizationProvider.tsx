// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { PropsWithChildren } from "react";
import { I18nextProvider, useTranslation } from 'react-i18next';
import { LocalizationContext } from './LocalizationContext';

// Create a provider component
export const LocalizationProvider: React.FC<PropsWithChildren<LocalizationContext>> = ({userPreferredLanguage, children}) => {

  const { i18n } = useTranslation();
  i18n.changeLanguage(userPreferredLanguage);

  return (
    <I18nextProvider i18n={i18n}>
      {children}
    </I18nextProvider>
  );
};