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
    appIcon: {
      ...theme.fonts.large,
      border: 'solid 1px',
      color: theme.palette.themePrimary,
      borderRadius: '50%',
      background: theme.palette.white,
      boxSizing: 'border-box',
      textAlign: 'center', 
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
