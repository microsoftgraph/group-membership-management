// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IStrings } from './IStrings';

export const defaultLanguage: string = 'en';
export const defaultStrings: IStrings = require(`./i18n/locales/${defaultLanguage}/translations`).strings;
