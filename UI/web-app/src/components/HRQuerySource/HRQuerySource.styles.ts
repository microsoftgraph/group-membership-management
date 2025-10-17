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
    expandButton: {
      fontSize: 12,
      height: 14
    },
    separator: {
      minWidth: 1200
    },
    cardHeader: {
      display: 'flex',
      flexDirection: 'row',
      alignItems: 'space-between',
      marginTop: 50,
      minWidth: 1200
    },
    cardTitle: {
      flex: 1,
      fontSize: 16,
      fontStyle: 'normal',
      fontWeight: 'normal',
      lineHeight: '22px'
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
      minWidth: 200
    },
    textFieldGroup: {
      border: 'none'
    },
    detailsList: {
      minWidth: 1200
    },
    detailsListColumnHeader:{
      fontWeight: 600,
      fontSize: 14,
      paddingLeft: 24
    },
    betweenGroupsDropdown: {
      width: 100,
      marginLeft: 0
    },
    betweenChildrenDropdown: {
      maxWidth: 100,
      marginLeft: 70
    },
    startOfNestedGroupDropdown: {
      maxWidth: 100,
      marginLeft: 70
    },
    endOfNestedGroupDropdown: {
      maxWidth: 100,
      marginLeft: -50
    },
    dropdownTitle: {
      borderRadius: 4,
      borderStyle: 'solid',
      borderWidth: 1,
      borderColor: theme.palette.neutralQuaternary,
      minWidth: 200
    },
    upDown: {
      display: 'flex',
      flexDirection: 'column'
    },
    addAttribute: {
      marginLeft: 60
    },
    removeButton: {
      color: theme.semanticColors.primaryButtonBackground,
      borderColor: theme.semanticColors.primaryButtonBackground,
      borderRadius: 4,
      border: 'none'
    },
    removeButtonDisabled: {
      opacity: 0.5,
      cursor: 'not-allowed'
    },
    error: {
      fontWeight: 400,
      fontSize: 12,
      lineHeight: 16,
      fontFamily: 'Segoe UI',
      color: theme.semanticColors.errorText
    },
    comboBoxOptionCodeText: {
      fontStyle: 'italic',
    },
    comboBoxOptionContainer: {
      paddingTop: 3,
      paddingBottom: 3,
    },
    comboBoxOptionList: {
      maxHeight: 300,
    },
    errorMessageStyles: {
      whiteSpace: 'normal',
      wordWrap: 'break-word',
      overflowWrap: 'break-word',
    },
    content: {
      maxHeight: '400px',
      overflowY: 'auto',
      padding: '0 20px'
    },
    generateTitleHeader: {
      display: 'flex',
      marginBottom: '10px'
    },
    generateTitleButton: {
      width: '150px',
      fontWeight: 'bold'
    },
    generateTitleSpinner: {
      marginLeft: '20px',
      fontWeight: 'bold'
    },
  };
};
