// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { getGlobalClassNames } from '@fluentui/react';

import { type IAppStyleProps, type IAppStyles } from './App.types';

const GlobalClassNames = {
  root: 'gmm-app',
};

export const getStyles = (props: IAppStyleProps): IAppStyles => {
  const { className, theme } = props;
  const classNames = getGlobalClassNames(GlobalClassNames, theme);

  return {
    root: [
      {
        display: 'flex',
        flexDirection: 'column',
        justifyContent: 'center',
        boxSizing: 'border-box',
        margin: '0 auto',
        fontFamily: 'Segoe UI',
        backgroundColor: theme.palette.neutralLighter,
        minHeight: '100vh',
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
