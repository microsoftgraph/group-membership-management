// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IChoiceGroupOption, IComboBoxOption, IDropdownOption } from '@fluentui/react';
import { strings } from '../services/localization/i18n/locales/en/translations';

export interface IExtendedDropdownOption extends IDropdownOption {
    description?: string;
}

export const nullOptions: IComboBoxOption[] = [
    { key: 'NULL', text: 'NULL' },
    { key: 'NOT NULL', text: 'NOT NULL' }
];

export const yesNoOptions: IChoiceGroupOption[] = [
    { key: 'Yes', text: strings.yes },
    { key: 'No', text: strings.no }
];

export const orAndOperatorOptions: IDropdownOption[] = [
    { key: '', text: '' },
    { key: 'Or', text: strings.or },
    { key: 'And', text: strings.and }
];

export const equalityOperatorOptions: IExtendedDropdownOption[] = [
    { key: '=', text: '=', description: strings.HROnboarding.equalToDescription },
    { key: '<', text: '<', description: strings.HROnboarding.lessThanDescription },
    { key: '<=', text: '<=', description: strings.HROnboarding.lessThanOrEqualDescription },
    { key: '>', text: '>', description: strings.HROnboarding.greaterThanDescription },
    { key: '>=', text: '>=', description: strings.HROnboarding.greaterThanOrEqualDescription },
    { key: '<>', text: '<>', description: strings.HROnboarding.notEqualToDescription },
    //{ key: 'IS', text: 'IS' },
    { key: 'IN', text: 'IN', description: strings.HROnboarding.inDescription },
    { key: 'NOT IN', text: 'NOT IN', description: strings.HROnboarding.notInDescription }
];