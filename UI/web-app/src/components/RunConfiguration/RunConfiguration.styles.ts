// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IRunConfigurationStyleProps,
    type IRunConfigurationStyles,
  } from './RunConfiguration.types';

  export const getStyles = (props: IRunConfigurationStyleProps): IRunConfigurationStyles => {
    const { className, theme } = props;

    return {
      root: [{
        fontWeight: 400,
        fontSize: 16,
        lineHeight: 22,
        fontFamily: 'Segoe UI',
        gap: 16,
        display: 'flex',
        flexDirection: 'column',
        paddingTop: 18,
        paddingBottom: 18,
        paddingLeft: 22,
        paddingRight: 22,
        borderRadius: 10,
        marginBottom: 12,
        backgroundColor: theme.palette.white
      }, className],
      horizontalChoiceGroup: {
        display: 'flex',
        flexDirection: 'row',
      },
      horizontalChoiceGroupContainer: {
        display: 'flex',
        flexDirection: 'row',
        '> *': { marginRight: '20px' }
      },
      controlWidth: {
        width: '30%',
      },
      frequencyLabel: {
        fontWeight: 'normal'
      },
      horizontalCheckboxes: {
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'center'
      },
      checkboxDropdownPair: {
        display: 'flex',
        flexDirection: 'column',
        marginRight: '20px',
        gap: 6
      },
      checkboxPairsContainer: {
        display: 'flex',
        flexDirection: 'row',
        width: '500px',
      },
      thresholdDropdown:{
        marginLeft: '30px',
      },
      dropdownTitle:{
        borderRadius: 4,
        borderStyle: 'solid',
        borderWidth: 1,
        borderColor: theme.palette.neutralQuaternary,
        background: theme.palette.white,
      },
      textFieldFieldGroup: {
        borderRadius: 4,
        border: '1px solid',
        borderColor: theme.palette.neutralQuaternary,
        backgroud: theme.palette.white,
        width: 300
      },
      thresholdWarning: {
        width: 'fit-content',
        display: 'flex',
        alignItems: 'center'
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
        minWidth: 200,
        width: '30%',
      },
      textFieldGroup: {
        border: 'none'
      },
    };
  };

