// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { getGlobalClassNames, getTheme } from '@fluentui/react';

import { type IAppStyleProps, type IAppStyles } from './App.types';

const GlobalClassNames = {
  root: 'gmm-app',
};

export const getStyles = (props: IAppStyleProps): IAppStyles => {
  const { className, theme } = props;
  const currentTheme = theme ?? getTheme();
  const neutralLighter = currentTheme?.palette?.neutralLighter ?? '#f3f2f1';
  const classNames = getGlobalClassNames(GlobalClassNames, currentTheme);

  return {
    root: [
      {
        display: 'flex',
        flexDirection: 'column',
        justifyContent: 'center',
        boxSizing: 'border-box',
        margin: '0 auto',
        fontFamily: 'Segoe UI',
        backgroundColor: neutralLighter,
        minHeight: '100vh',
        selectors: {
          '[role="columnheader"].is-actionable, [role="columnheader"].is-actionable *': {
            cursor: 'pointer',
          },
          '[role="columnheader"].ms-DetailsHeader-cellIsCheck, [role="columnheader"].ms-DetailsHeader-cellIsCheck *': {
            cursor: 'pointer',
          },
          'th a, th a *, th button, th button *, th [role="button"], th [role="button"] *': {
            cursor: 'pointer',
          },
        },
      },
      classNames.root,
      className,
    ],
    content: {
      width: '100%',
      flexGrow: 1,
    },
    permissionDenied: {
      display: 'flex',
      justifyContent: 'center',
      alignItems: 'center',
      width: '100%',
      height: '50vh'
    }
  };
};
