// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { PropsWithChildren } from 'react';
import i18n from 'i18next';
import { initReactI18next, I18nextProvider } from 'react-i18next';
import { LocalizationInternalProvider } from './LocalizationInternalProvider';

// setup
i18n.use(initReactI18next).init({
  returnNull: false,
  fallbackLng: 'en',
  lng: 'en',
  resources: {
    en: {
      translations: require('./i18n/locales/en/translations').strings,
    },
    es: {
      translations: require('./i18n/locales/es/translations').strings,
    },
  },
  ns: ['translations'],
  defaultNS: 'translations',
});

i18n.languages = ['en', 'es'];

// Create a provider component
export const LocalizationProvider: React.FC<PropsWithChildren> = ({
  children,
}) => {
  return (
    <I18nextProvider i18n={i18n}>
      <LocalizationInternalProvider>{children}</LocalizationInternalProvider>
    </I18nextProvider>
  );
};
