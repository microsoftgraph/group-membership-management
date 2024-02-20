// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { HRQuerySourceStyleProps, HRQuerySourceStyles } from './HRQuerySource.types';

export const getStyles = (props: HRQuerySourceStyleProps): HRQuerySourceStyles => {
  const { className, theme } = props;

  return {
    root: [{
      fontWeight: 400,
      fontSize: 16,
      lineHeight: 22,
      fontFamily: 'Segoe UI',
      display: 'flex',
      flexDirection: 'column',
      width: '100%',
      maxWidth: 600
    }, className],
    horizontalChoiceGroup: {
      display: 'flex',
      flexDirection: 'row'
    },
    horizontalChoiceGroupContainer: {
      display: 'flex',
      flexDirection: 'row',
      '> *': { marginRight: '20px' },
      flexWrap: 'wrap'
    },
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
      borderColor: theme.palette.neutralQuaternary
    },
    textFieldGroup: {
      border: 'none'
    },
    spinButton: {
      '&:after': {
        borderColor: theme.palette.neutralQuaternary
      },
      selectors: {
        [`@media (max-width: 600px)`]: {
          width: 10
        }
      }
    },
    error: {
      fontWeight: 400,
      fontSize: 12,
      lineHeight: 16,
      fontFamily: 'Segoe UI',
      color: theme.semanticColors.errorText
    }
  };
};