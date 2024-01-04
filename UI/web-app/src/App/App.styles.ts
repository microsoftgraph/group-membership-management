// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { getGlobalClassNames } from '@fluentui/react';

import { type IAppStyleProps, type IAppStyles } from './App.types';

const GlobalClassNames = {
  root: 'gmm-app',
};

export const getStyles = (props: IAppStyleProps): IAppStyles => {
  const { className, theme } = props;
  const footerHeight = '100px';
  const classNames = getGlobalClassNames(GlobalClassNames, theme);

  return {
    root: [{
      fontFamily: 'Segoe UI',
      backgroundColor: theme.palette.neutralLighter,
      height: '100vh',
      overflow: 'hidden'
    }, classNames.root, className],
    body: {
      display: 'flex',
      flexDirection: 'row',
      flexGrow: 1,
      justifyContent: 'center',
      boxSizing: 'border-box',
      margin: '0 auto',
      height: `calc(100vh - ${footerHeight})`,
      overflow: 'auto',
    },
    content: {
      width: '100%',
      overflow: 'auto',
    }
  };
};
