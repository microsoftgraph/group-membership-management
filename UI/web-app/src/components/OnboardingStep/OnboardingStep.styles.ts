// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IOnboardingStepStyleProps,
    type IOnboardingStepStyles,
  } from './OnboardingStep.types';

  export const getStyles = (props: IOnboardingStepStyleProps): IOnboardingStepStyles => {
    const { className, theme, flushWithContent } = props;

    return {
      root: [{
      }, className],
      titleCard: {
        paddingTop: 18,
        paddingBottom: flushWithContent ? 0 : 18,
        paddingLeft: 22,
        paddingRight: 22,
        borderRadius: 10,
        borderBottomLeftRadius: flushWithContent ? 0 : 10,
        borderBottomRightRadius: flushWithContent ? 0 : 10,
        marginBottom: flushWithContent ? 0 : 12,
        backgroundColor: theme.palette.white
      },
      stepTitle: {
        fontWeight: 600,
        fontSize: 20,
        fontFamily: 'Segoe UI',
      },
      stepTitleRow: {
        display: 'flex',
        alignItems: 'center',
        marginBottom: 8,
        gap: 16
      },
      stepDescription: {
        fontWeight: 400,
        fontSize: 16,
        fontFamily: 'Segoe UI'
      },
      headerDivider: {
        padding: 0,
        marginTop: flushWithContent ? 0 : 8,
        selectors: {
          '::before': {
            backgroundColor: theme.palette.neutralQuaternaryAlt
          }
        }
      }
    };
  };

