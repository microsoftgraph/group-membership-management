// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { UserSpotCheckStyleProps, UserSpotCheckStyles } from './UserSpotCheck.types';

export const getStyles = (props: UserSpotCheckStyleProps): UserSpotCheckStyles => {
  const { theme } = props;

  return {
    root: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      backgroundColor: theme.palette.white,
      padding: 16,
      marginBottom: 16,
      width: '35%',
    },
    title: {
      fontWeight: 600,
      fontSize: 14,
      marginBottom: 4,
    },
    description: {
      fontSize: 12,
      color: theme.palette.neutralSecondary,
      marginBottom: 8,
    },
    picker: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      backgroundColor: theme.palette.white,
      maxWidth: '50vw',
    },
    resultsContainer: {
      marginTop: 12,
      display: 'flex',
      flexDirection: 'column',
      gap: 8,
    },
    partRow: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      padding: '6px 8px',
      borderRadius: 4,
      backgroundColor: theme.palette.neutralLighterAlt,
    },
    partTitle: {
      fontSize: 13,
      fontWeight: 500,
      marginRight: 12,
      wordBreak: 'break-word',
    },
    statusBadge: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 4,
      fontSize: 13,
      whiteSpace: 'nowrap',
      cursor: 'default',
    },
    statusIcon: {
      fontSize: 14,
    },
    errorMessage: {
      marginTop: 12,
    },
    banner: {
      marginTop: 12,
    },
    spinnerContainer: {
      display: 'flex',
      alignItems: 'center',
      gap: 8,
      marginTop: 12,
    },
  };
};
