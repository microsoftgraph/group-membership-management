// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import i18next, { CustomTypeOptions, i18n } from 'i18next';

import { ILocalizationService } from './ILocalizationService';
import { IStrings } from './IStrings';
import { strings as enStrings } from './i18n/locales/en/translations';
import { strings as esStrings } from './i18n/locales/es/translations';

export class LocalizationService implements ILocalizationService {
  private _i18n: i18n = i18next;
  constructor() {
    this._i18n.init<CustomTypeOptions>({
      returnNull: false,
      fallbackLng: 'en',
      lng: 'en',
      resources: {
        en: {
          translations: enStrings,
        },
        es: {
          translations: esStrings,
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
};
