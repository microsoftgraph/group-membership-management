// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, {
  PropsWithChildren,
  useContext,
  useEffect,
  useState,
} from 'react';
import { useTranslation } from 'react-i18next';
import { LocalizationContext } from './LocalizationContext';
import { IStrings } from './IStrings';

// Create a provider component
export const LocalizationInternalProvider: React.FC<PropsWithChildren> = ({
  children,
}) => {
  const { i18n } = useTranslation();
  const [currentLanguage, setCurrentLanguage] = useState<string>('en');
  const [strings, setStrings] = useState<IStrings>(
    i18n.getResourceBundle(i18n.language, 'translations')
  );

  const setUserPreferredLanguage = (userPreferredLanguage: string = 'en') => {
    setCurrentLanguage(userPreferredLanguage);
  };

  useEffect(() => {
    i18n.changeLanguage(currentLanguage);
    setStrings(i18n.getResourceBundle(i18n.language, 'translations'));
  }, [i18n, currentLanguage]);

  return (
    <LocalizationContext.Provider value={{ setUserPreferredLanguage, strings }}>
      {children}
    </LocalizationContext.Provider>
  );
};

export const useLocalization = () => useContext(LocalizationContext);
export const useStrings = () => useContext(LocalizationContext).strings;
