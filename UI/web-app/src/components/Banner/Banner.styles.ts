// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IBannerStyleProps, type IBannerStyles } from './Banner.types';

export const getStyles = (props: IBannerStyleProps): IBannerStyles => {
  const { className, theme } = props;

  return {
    root: [
      {
        display: 'flex',
        ...theme.fonts.medium, // 14
        backgroundColor: theme.palette.themeLight,
        color: theme.semanticColors.bodyText,
        padding: 10,
        justifyContent: 'flex-end',
        alignItems: 'center',
        gap: 10,
      },
      className,
    ],
    icon: {
      ...theme.fonts.xLarge,
      height: 20,
      width: 20
    },
    message: {
      lineHeight: 20,
      textAlign: 'right',
      paddingRight: 10,
      borderRight: '1px solid',
      borderRightColor: theme.palette.themePrimary
    },
    messageContainer: {
      display: 'flex',
      gap: 10,
    },
    toggle: {
      color: theme.semanticColors.bodyText,
      height: 24,
      width: 24
    },
  };
};
