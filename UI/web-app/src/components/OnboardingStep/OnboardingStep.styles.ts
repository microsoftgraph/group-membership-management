// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IOnboardingStepStyleProps,
    type IOnboardingStepStyles,
  } from './OnboardingStep.types';

  export const getStyles = (props: IOnboardingStepStyleProps): IOnboardingStepStyles => {
    const { className, theme, flushWithContent, singleCard } = props;
    const isFlush = flushWithContent || singleCard;

    return {
      root: [{
      }, className],
      titleCard: {
        paddingTop: 18,
        paddingBottom: isFlush ? 0 : 18,
        paddingLeft: 22,
        paddingRight: 22,
        borderRadius: 10,
        borderBottomLeftRadius: isFlush ? 0 : 10,
        borderBottomRightRadius: isFlush ? 0 : 10,
        marginBottom: isFlush ? 0 : 12,
        backgroundColor: theme.palette.white
      },
      contentCard: singleCard ? {
        paddingLeft: 22,
        paddingRight: 22,
        paddingTop: 18,
        paddingBottom: 18,
        borderBottomLeftRadius: 10,
        borderBottomRightRadius: 10,
        marginBottom: 12,
        backgroundColor: theme.palette.white
      } : {},
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
        marginTop: isFlush ? 0 : 8,
        selectors: {
          '::before': {
            backgroundColor: theme.palette.neutralQuaternaryAlt
          }
        }
      }
    };
  };

