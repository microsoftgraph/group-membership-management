// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { OnboardingStepBase } from './OnboardingStep.base';
import { getStyles } from './OnboardingStep.styles';
import {
  type IOnboardingStepProps,
  type IOnboardingStepStyleProps,
  type IOnboardingStepStyles,
} from './OnboardingStep.types';

export const OnboardingStep: React.FunctionComponent<IOnboardingStepProps> = styled<
  IOnboardingStepProps,
  IOnboardingStepStyleProps,
  IOnboardingStepStyles
>(OnboardingStepBase, getStyles, undefined, {
  scope: 'OnboardingStep',
});
