// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it, beforeEach, vi } from 'vitest';
import themeReducer, { toggleTheme, setTheme, selectIsDarkMode } from './theme.slice';

// Mock localStorage
const localStorageMock = {
  store: {} as Record<string, string>,
  getItem: vi.fn((key: string) => localStorageMock.store[key] ?? null),
  setItem: vi.fn((key: string, value: string) => { localStorageMock.store[key] = value; }),
  removeItem: vi.fn((key: string) => { delete localStorageMock.store[key]; }),
  clear: vi.fn(() => { localStorageMock.store = {}; }),
  get length() { return Object.keys(localStorageMock.store).length; },
  key: vi.fn(() => null),
};
Object.defineProperty(globalThis, 'localStorage', { value: localStorageMock, writable: true });

const initial = themeReducer(undefined, { type: '@@INIT' });

describe('theme.slice', () => {
  beforeEach(() => {
    localStorageMock.store = {};
    vi.clearAllMocks();
  });

  it('toggleTheme flips isDarkMode', () => {
    const state1 = themeReducer({ isDarkMode: false }, toggleTheme());
    expect(state1.isDarkMode).toBe(true);
    const state2 = themeReducer(state1, toggleTheme());
    expect(state2.isDarkMode).toBe(false);
  });

  it('toggleTheme saves to localStorage', () => {
    themeReducer({ isDarkMode: false }, toggleTheme());
    expect(localStorageMock.setItem).toHaveBeenCalledWith('gmmThemeMode', 'dark');
  });

  it('setTheme sets isDarkMode', () => {
    const state = themeReducer({ isDarkMode: false }, setTheme(true));
    expect(state.isDarkMode).toBe(true);
  });

  it('setTheme saves to localStorage', () => {
    themeReducer({ isDarkMode: true }, setTheme(false));
    expect(localStorageMock.setItem).toHaveBeenCalledWith('gmmThemeMode', 'light');
  });

  it('selectIsDarkMode returns isDarkMode', () => {
    const root = { theme: { isDarkMode: true } } as any;
    expect(selectIsDarkMode(root)).toBe(true);
  });
});
