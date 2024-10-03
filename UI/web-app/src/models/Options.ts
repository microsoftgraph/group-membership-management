// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IChoiceGroupOption, IComboBoxOption, IDropdownOption } from "@fluentui/react";
import { strings } from "../services/localization/i18n/locales/en/translations";

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

export const equalityOperatorOptions: IDropdownOption[] = [
    { key: '=', text: '=' },
    { key: '<', text: '<' },
    { key: '<=', text: '<=' },
    { key: '>', text: '>'},
    { key: '>=', text: '>=' },
    { key: '<>', text: '<>' },
    { key: 'IS', text: 'IS' },
    { key: 'IN', text: 'IN' }
];