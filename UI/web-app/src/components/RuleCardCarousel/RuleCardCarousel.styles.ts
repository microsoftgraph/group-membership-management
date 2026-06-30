// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type RuleCardCarouselStyleProps, type RuleCardCarouselStyles } from './RuleCardCarousel.types';

export const getStyles = (props: RuleCardCarouselStyleProps): RuleCardCarouselStyles => {
  const { className, theme } = props;

  return {
    root: [
      {
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'stretch',
        gap: 8,
        width: '100%',
        position: 'relative',
      },
      className,
    ],
    track: {
      display: 'flex',
      flexDirection: 'row',
      gap: 16,
      overflowX: 'auto',
      scrollBehavior: 'smooth',
      flex: '1 1 auto',
      padding: 4,
      alignItems: 'stretch',
      selectors: {
        '::-webkit-scrollbar': {
          height: 8,
        },
        '::-webkit-scrollbar-thumb': {
          backgroundColor: theme.palette.neutralTertiaryAlt,
          borderRadius: 4,
        },
      },
    },
    scrollButton: {
      flex: '0 0 auto',
      alignSelf: 'center',
      backgroundColor: theme.palette.themePrimary,
      color: theme.palette.white,
      borderRadius: 4,
      height: 48,
      width: 28,
      selectors: {
        ':hover': {
          backgroundColor: theme.palette.themeDarkAlt,
          color: theme.palette.white,
        },
        ':disabled': {
          backgroundColor: theme.palette.neutralLighter,
          color: theme.palette.neutralTertiary,
        },
        '.ms-Button-icon': {
          color: 'inherit',
        },
      },
    },
    caret: {
      position: 'absolute',
      bottom: -13,
      width: 0,
      height: 0,
      borderLeft: '13px solid transparent',
      borderRight: '13px solid transparent',
      borderTop: `13px solid ${theme.palette.themePrimary}`,
      transform: 'translateX(-50%)',
      pointerEvents: 'none',
      transition: 'left 0.15s ease',
    },
  };
};
