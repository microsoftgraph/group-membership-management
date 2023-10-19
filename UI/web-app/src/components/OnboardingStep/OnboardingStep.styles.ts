// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IOnboardingStepStyleProps,
    type IOnboardingStepStyles,
  } from './OnboardingStep.types';
  
  export const getStyles = (props: IOnboardingStepStyleProps): IOnboardingStepStyles => {
    const { className, theme } = props;
  
    return {
      root: [{
      }, className],
      card: {
        paddingTop: 18,
        paddingBottom: 18,
        paddingLeft: 22,
        paddingRight: 22,
        borderRadius: 10,
        marginBottom: 12,
        backgroundColor: theme.palette.white
      },
      toggleContainer:{
        display: 'flex',
        flexDirection: 'row',
        justifyContent: 'flex-end',
      },
      title: {
        fontWeight: 600,
        fontSize: 24,
        fontFamily: 'Segoe UI',
        marginBottom: 14
      },
      stepTitle: {
        fontWeight: 600,
        fontSize: 20,
        fontFamily: 'Segoe UI',
        marginBottom: 8
      },
      stepDescription: {
        fontWeight: 400,
        fontSize: 16,
        fontFamily: 'Segoe UI'
      },
      destination: {
        fontWeight: 400,
        fontSize: 16,
        fontFamily: 'Segoe UI',
        marginBottom: 14
      }
    };
  };
  