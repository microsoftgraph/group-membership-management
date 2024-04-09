// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { HRFilterStyleProps, HRFilterStyles } from './HRFilter.types';

export const getStyles = (props: HRFilterStyleProps): HRFilterStyles => {
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
      borderColor: theme.palette.neutralQuaternary,
      minWidth: 250
    },
    textFieldGroup: {
      border: 'none'
    },
    removeButton: {
      marginLeft: 'auto',
      color: theme.semanticColors.primaryButtonBackground,
      borderColor: theme.semanticColors.primaryButtonBackground,
      borderRadius: 4,
      border: 'none',
      marginTop: '25px'
    },
    dropdownTitle: {
      borderRadius: 4,
      borderStyle: 'solid',
      borderWidth: 1,
      borderColor: theme.palette.neutralQuaternary,
      minWidth: 250
    },
    comboBoxHover: {      
      '&:after': {
        borderColor: theme.palette.neutralQuaternary
      }
    },
    comboBoxTitle: {
      borderRadius: 4,
      borderStyle: 'solid',
      borderWidth: 1,
      border: 'none',
      minWidth: 250
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