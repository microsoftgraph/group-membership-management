// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from "react";
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import {
  IOnboardingStepProps,
  IOnboardingStepStyleProps,
  IOnboardingStepStyles,
} from './OnboardingStep.types';
import { useStrings } from "../../store/hooks";
import { PageSection } from "../PageSection";
import { useSelector } from "react-redux";
import { manageMembershipSelectedDestinationName, manageMembershipSelectedDestinationType } from "../../store/manageMembership.slice";

const getClassNames = classNamesFunction<
  IOnboardingStepStyleProps,
  IOnboardingStepStyles
>();

export const OnboardingStepBase: React.FunctionComponent<IOnboardingStepProps> = (props) => {
  const { className, styles, children, stepTitle, stepDescription } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<IOnboardingStepStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const destinationType = useSelector(manageMembershipSelectedDestinationType);
  const destinationName = useSelector(manageMembershipSelectedDestinationName);

  const destinationTypeLabel: string =
    destinationType === "GroupMembership" ?
      strings.ManageMembership.labels.group
      : destinationType ? destinationType.toString() : '';

  return (
    <div className={classNames.root}>
      <div className={classNames.titleCard}>
        <PageSection>
          <div className={classNames.title}>{strings.ManageMembership.labels.pageTitle}</div>
          {(destinationType && destinationName) && (<div className={classNames.destination}>{destinationTypeLabel}: {destinationName}</div>)}
          <div className={classNames.stepTitle}>{stepTitle}</div>
          <div className={classNames.stepDescription}>{stepDescription}</div>
        </PageSection>
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
