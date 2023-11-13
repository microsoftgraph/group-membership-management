// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from "react";
import {
  IProcessedStyleSet,
  Toggle,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import {
  IOnboardingStepProps,
  IOnboardingStepStyleProps,
  IOnboardingStepStyles,
} from './OnboardingStep.types';
import { useStrings } from "../../localization/";
import { PageSection } from "../PageSection";

const getClassNames = classNamesFunction<
  IOnboardingStepStyleProps,
  IOnboardingStepStyles
>();

export const OnboardingStepBase: React.FunctionComponent<IOnboardingStepProps> = (props) => {
  const { className, styles, children, stepTitle, stepDescription, destinationType, destinationName } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<IOnboardingStepStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  const isAdvancedQueryChild = React.Children.toArray(children).some(child =>
    React.isValidElement(child) && (child.type as React.ComponentType).displayName === 'StyledAdvancedQueryBase'
  );

  return (
    <div className={classNames.root}>
      <div className={classNames.card}>
        <PageSection>
          <div className={classNames.title}>{strings.ManageMembership.labels.pageTitle}</div>
          {(destinationType && destinationName) && (<div className={classNames.destination}>{destinationType}: {destinationName}</div>)}
          <div className={classNames.stepTitle}>{stepTitle}</div>
          <div className={classNames.stepDescription}>{stepDescription}</div>
        </PageSection>
      </div>
      {isAdvancedQueryChild &&
        <div className={classNames.toggleContainer}>
          <Toggle
            inlineLabel
            defaultChecked disabled
            onText={strings.ManageMembership.labels.advancedView}
            offText={strings.ManageMembership.labels.advancedView} />
        </div>
      }
      <div className={classNames.card}>
        <PageSection>
          <div>
            {children}
          </div>
        </PageSection>
      </div>
    </div>
  );
};
