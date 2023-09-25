// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IAppHeaderStyleProps,
  type IAppHeaderStyles,
} from './AppHeader.types';

export const getStyles = (props: IAppHeaderStyleProps): IAppHeaderStyles => {
  const { className, theme } = props;

  return {
    root: [
      {
        display: 'flex',
        justifyContent: 'space-between',
        backgroundColor: theme.palette.themePrimary,
        paddingLeft: 37,
        color: theme.palette.white
      },
      className,
    ],
    mainButton:{
      textDecoration: 'none',
      color: 'inherit',
      padding: 0,
      margin: 0,
      fontSize: 'inherit',
      fontStyle: 'inherit',
      fontWeight: 'inherit'
    },
    appIcon: {
      boxSizing: 'border-box',
      height: 32,
      width: 32,
      margin: '8px 0px'
    },
    appTitle: {
      ...theme.fonts.mediumPlus,
      lineHeight: 32,
      padding: '8px 0px'
    },
    settingsContainer: {
      display: 'flex',
      justifyContent: 'flex-end',
    },
    settingsIcon: {
      ...theme.fonts.large,
      color: theme.palette.white,
      height: 48,
      width: 48
    },
    titleContainer: {
      display: 'flex',
      gap: 10
    },
    userPersona: {
      height: 48,
      width: 48
    }
  };
};
