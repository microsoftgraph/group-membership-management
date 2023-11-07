// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import i18next, { CustomTypeOptions, i18n } from 'i18next';

import { ILocalizationService } from './ILocalizationService';
import { IStrings } from './IStrings';

export class LocalizationService implements ILocalizationService {
  private _i18n: i18n = i18next;
  constructor() {
    this._i18n.init<CustomTypeOptions>({
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

    this._i18n.languages = ['en', 'es'];
  }

  public changeLanguage(language: string): void {
    this._i18n.changeLanguage(language);
  }

  public getStrings(): IStrings {
    return this._i18n.getResourceBundle(this._i18n.language, 'translations');
  }
}
