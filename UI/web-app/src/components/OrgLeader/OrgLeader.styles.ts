// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { OrgLeaderStyleProps, OrgLeaderStyles } from './OrgLeader.types';

export const getStyles = (props: OrgLeaderStyleProps): OrgLeaderStyles => {
  const { className, theme } = props;

  return {
    root: [
      {
        display: 'flex',
        flexDirection: 'column',
        width: '100%'
      },
      className,
    ],
    labelContainer: {
      display: 'flex',
      alignItems: 'center'
    },
    textField: {
      fontWeight: 300,
      fontSize: 16,
      fontFamily: 'Segoe UI',
      borderRadius: 4,
      borderStyle: 'solid',
      borderWidth: 1,
      borderColor: theme.palette.neutralQuaternary,
      minWidth: 200
    },
    textFieldGroup: {
      border: 'none'
    },
    suggestionItems: {
      '.ms-Persona-secondaryText': {
        whiteSpace: 'normal',
        overflow: 'visible',
        textOverflow: 'clip'
      }
    },
    error: {
      fontWeight: 400,
      fontSize: 12,
      lineHeight: 16,
      fontFamily: 'Segoe UI',
      color: theme.semanticColors.errorText
    },
  };
};