// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IAppHeaderStyleProps, IAppHeaderStyles } from './AppHeader.types';

export const getStyles = (props: IAppHeaderStyleProps): IAppHeaderStyles => {
  const { className, theme } = props;

  return {
    root: [
      {
        backgroundColor: theme.palette.themePrimary,
        height: 40,
        color: theme.palette.white,
        maxWidth: "100%",
        margin: '0 auto',
        borderBottom: '1px solid transparent',
        boxSizing: 'border-box',
        paddingLeft: 20,
        paddingTop: 10
      },
      className
    ],
    circle: {
      border: 'solid 2px',
      borderRadius: '50%',
      background: theme.palette.white,
      height: 20,
      width: 20,
      display: 'flex',
      justifyContent: 'center',
      alignItems: 'center'
    },
    tabContent: {
      color: theme.palette.white,
      float: 'left',
      border: 'none',
      outline: 'none',
      cursor: 'pointer',
      paddingLeft: 15
    },
    icon: {
      color: theme.palette.themePrimary,
      paddingBottom: 5,
      paddingRight: 0.5
    },
    right: {
      paddingRight: 10,
      fontSize: 15,
      float: 'right'
    },
    welcome: {
      fontWeight: 'bold',
      fontSize: 25,
      paddingLeft: 20
    },
    learn: {
      paddingLeft: 20

    },
    whole: {
      backgroundColor: theme.palette.neutralLight
    }
  };
};
