// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { PropsWithChildren } from 'react';
import { render } from '@testing-library/react';
import type { RenderOptions } from '@testing-library/react';
import type { PreloadedState } from '@reduxjs/toolkit';
import { Provider } from 'react-redux';

import type { Services } from '../services';
import { setupStore } from '../store';
import type { AppStore, RootState } from '../store';

// This type interface extends the default options for render from RTL, as well
// as allows the user to specify other things such as initialState, store.
interface ExtendedRenderOptions extends Omit<RenderOptions, 'queries'> {
  preloadedState?: PreloadedState<RootState>;
  serviceMocks?: Partial<Services>;
  store?: AppStore;
}

export function renderWithProviders(
  ui: React.ReactElement,
  {
    preloadedState = {},
    serviceMocks = {},
    // Automatically create a store instance if no store was passed in
    store = setupStore(preloadedState, serviceMocks),
    ...renderOptions
  }: ExtendedRenderOptions = {}
) {
  function Wrapper({ children }: PropsWithChildren<{}>): JSX.Element {
    return <Provider store={store}>{children}</Provider>;
  }
  return { store, ...render(ui, { wrapper: Wrapper, ...renderOptions }) };
}
