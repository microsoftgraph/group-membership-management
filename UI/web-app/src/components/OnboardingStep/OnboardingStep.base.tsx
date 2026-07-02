// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
  Separator,
} from '@fluentui/react';
import {
  IOnboardingStepProps,
  IOnboardingStepStyleProps,
  IOnboardingStepStyles,
} from './OnboardingStep.types';
import { PageSection } from '../PageSection';

const getClassNames = classNamesFunction<
  IOnboardingStepStyleProps,
  IOnboardingStepStyles
>();

export const OnboardingStepBase: React.FunctionComponent<IOnboardingStepProps> = (props) => {
  const { className, styles, children, stepTitle, stepDescription, headerAction, flushWithContent } = props;

  const classNames: IProcessedStyleSet<IOnboardingStepStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
      flushWithContent,
    }
  );

  return (
    <div className={classNames.root}>
      <div className={classNames.titleCard}>
        <PageSection>
          <div className={classNames.stepTitleRow}>
            <div className={classNames.stepTitle}>{stepTitle}</div>
            {headerAction}
          </div>
          <div className={classNames.stepDescription}>{stepDescription}</div>
        </PageSection>
        <Separator className={classNames.headerDivider} />
      </div>
      <div>
        <PageSection>
          <div>
            {children}
          </div>
        </PageSection>
      </div>
    </div>
  );
};
