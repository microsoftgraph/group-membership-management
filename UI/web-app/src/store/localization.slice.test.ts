// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import localizationReducer, { selectStrings } from './localization.slice';
import { setLanguage } from './localization.api';

const initial = localizationReducer(undefined, { type: '@@INIT' });

describe('localization.slice — extraReducers', () => {
  it('setLanguage.fulfilled sets language and strings', () => {
    const payload = { language: 'es', strings: { greeting: 'Hola' } };
    const state = localizationReducer(initial, setLanguage.fulfilled(payload as any, 'r1', 'es'));
    expect(state.language).toBe('es');
    expect((state.strings as any).greeting).toBe('Hola');
  });
});

describe('localization.slice — selectors', () => {
  it('selectStrings returns strings', () => {
    const root = { localization: initial } as any;
    expect(selectStrings(root)).toBe(initial.strings);
  });
});
