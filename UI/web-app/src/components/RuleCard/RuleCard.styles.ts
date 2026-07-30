// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type RuleCardStyleProps, type RuleCardStyles } from './RuleCard.types';

export const getStyles = (props: RuleCardStyleProps): RuleCardStyles => {
  const { className, theme, selected } = props;

  return {
    root: [
      {
        boxSizing: 'border-box',
        width: 280,
        minWidth: 280,
        flex: '0 0 auto',
        padding: 16,
        borderRadius: 8,
        border: `1px solid ${theme.palette.neutralQuaternary}`,
        backgroundColor: theme.palette.white,
        cursor: 'pointer',
        display: 'flex',
        flexDirection: 'column',
        gap: 10,
        selectors: {
          ':hover': {
            borderColor: theme.palette.themePrimary,
          },
          ':focus-visible': {
            outline: `2px solid ${theme.palette.themePrimary}`,
            outlineOffset: '2px',
          },
        },
      },
      selected && {
        border: `2px solid ${theme.palette.themePrimary}`,
        padding: 15,
      },
      className,
    ],
    selected: {},
    header: {
      display: 'flex',
      flexDirection: 'row',
      justifyContent: 'space-between',
      alignItems: 'flex-start',
      gap: 6,
    },
    actions: {
      display: 'flex',
      flexDirection: 'row',
      flex: '0 0 auto',
      gap: 2,
    },
    actionButton: {
      color: theme.palette.neutralSecondary,
      height: 28,
      width: 28,
      selectors: {
        ':hover': {
          color: theme.palette.themePrimary,
          backgroundColor: theme.palette.neutralLighter,
        },
      },
    },
    hiddenIndicator: {
      display: 'flex',
      flexDirection: 'row',
      alignItems: 'center',
      gap: 6,
    },
    hiddenIcon: {
      color: theme.semanticColors.errorText,
      fontSize: 14,
    },
    hiddenText: {
      color: theme.semanticColors.errorText,
      fontSize: 12,
      fontWeight: 600,
    },
    badges: {
      display: 'flex',
      flexDirection: 'row',
      flexWrap: 'wrap',
      gap: 6,
      alignItems: 'center',
    },
    inclusiveBadge: {
      backgroundColor: theme.palette.themePrimary,
      color: theme.palette.white,
      borderRadius: 12,
      padding: '2px 10px',
      fontSize: 12,
      fontWeight: 600,
      whiteSpace: 'nowrap',
    },
    exclusiveBadge: {
      backgroundColor: theme.palette.themeLighter,
      color: theme.palette.themeDarker,
      borderRadius: 12,
      padding: '2px 10px',
      fontSize: 12,
      fontWeight: 600,
      whiteSpace: 'nowrap',
    },
    typeBadge: {
      backgroundColor: theme.palette.neutralLighter,
      color: theme.palette.neutralPrimary,
      borderRadius: 12,
      padding: '2px 10px',
      fontSize: 12,
      fontWeight: 600,
      whiteSpace: 'nowrap',
    },
    title: {
      fontSize: 14,
      fontWeight: 600,
      fontFamily: 'Segoe UI',
      color: theme.palette.neutralPrimary,
      margin: 0,
      display: '-webkit-box',
      WebkitLineClamp: 2,
      WebkitBoxOrient: 'vertical',
      overflow: 'hidden',
    },
    detailRows: {
      display: 'flex',
      flexDirection: 'column',
      gap: 6,
      marginTop: 'auto',
    },
    detailRow: {
      display: 'flex',
      flexDirection: 'row',
      alignItems: 'center',
      gap: 6,
      fontSize: 13,
    },
    detailLabel: {
      color: theme.palette.neutralSecondary,
      whiteSpace: 'nowrap',
    },
    detailValue: {
      color: theme.palette.neutralPrimary,
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap',
    },
    detailSpinner: {
      justifyContent: 'flex-start',
    },
  };
};
