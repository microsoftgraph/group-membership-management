// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
  DefaultButton, PrimaryButton,
  Icon,
  Dialog, DialogType, DialogFooter, IComboBox, IComboBoxOption
} from '@fluentui/react';
import { useNavigate } from 'react-router-dom';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { IManageMembershipProps, IManageMembershipStyleProps, IManageMembershipStyles } from './ManageMembership.types';
import { AppDispatch } from '../../store';
import { useStrings } from '../../store/hooks';
import { OnboardingStep } from '../../components/OnboardingStep';
import { AdvancedQuery } from '../../components/AdvancedQuery';
import { SelectDestination } from '../../components/SelectDestination';
import { Destination } from '../../models/Destination';
import {
  manageMembershipIsGroupReadyForOnboarding,
  manageMembershipCurrentStep,
  manageMembershipHasChanges,
  manageMembershipIsQueryValid,
  manageMembershipQuery,
  manageMembershipSelectedDestination,
  setCurrentStep,
  setHasChanges,
  setSelectedDestination
} from '../../store/manageMembership.slice';
import { getGroupEndpoints, getGroupOnboardingStatus } from '../../store/manageMembership.api';

const getClassNames = classNamesFunction<
  IManageMembershipStyleProps,
  IManageMembershipStyles
>();

export const ManageMembershipBase: React.FunctionComponent<IManageMembershipProps> = (
  props: IManageMembershipProps
) => {
  const { className, styles } = props;

  const classNames: IProcessedStyleSet<IManageMembershipStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const strings = useStrings();
  const navigate = useNavigate();
  const dispatch = useDispatch<AppDispatch>();

  const [showLeaveManageMembershipDialog, setShowLeaveManageMembershipDialog] = useState(false);

  const currentStep = useSelector(manageMembershipCurrentStep);
  const hasChanges = useSelector(manageMembershipHasChanges);
  const selectedDestination = useSelector(manageMembershipSelectedDestination);
  const isGroupReadyForOnboarding = useSelector(manageMembershipIsGroupReadyForOnboarding);

  // Onboarding values
  const query = useSelector(manageMembershipQuery);
  const isQueryValid = useSelector(manageMembershipIsQueryValid);

  const handleSearchDestinationChange = async (event: React.FormEvent<IComboBox>, option?: IComboBoxOption) => {
    if (option) {
      dispatch(setHasChanges(true));
      const selectedGroupId = option.key as string;
      const selectedDestination: Destination = { id: selectedGroupId, name: option.text, type: 'Group' };

      dispatch(setSelectedDestination(selectedDestination));
      dispatch(getGroupEndpoints(selectedGroupId));
      dispatch(getGroupOnboardingStatus(selectedGroupId));
    }
  };

  const handleBackToDashboardButtonClick = () => {
    if (hasChanges)
      setShowLeaveManageMembershipDialog(true);
  };

  const onNextStepClick = () => {
    dispatch(setCurrentStep(currentStep + 1));
  };

  const onBackStepClick = () => {
    dispatch(setCurrentStep(currentStep - 1));
  };

  const onDialogClose = () => {
    setShowLeaveManageMembershipDialog(false);
  };

  const onConfirmExit = () => {
    navigate(-1);
    setShowLeaveManageMembershipDialog(false);
  };

  const isStep1ConditionsMet = selectedDestination && isGroupReadyForOnboarding === true;
  const isStep2ConditionsMet = isQueryValid;
  let isNextDisabled = false;

  if (currentStep === 1 && !isStep1ConditionsMet) {
    isNextDisabled = true;
  } else if (currentStep === 2 && !isStep2ConditionsMet) {
    isNextDisabled = true;
  } else if (currentStep === 3) {
    isNextDisabled = false;
  } else if (currentStep === 4) {
    isNextDisabled = true;
  }

  return (
    <Page>
      <PageHeader onBackToDashboardButtonClick={handleBackToDashboardButtonClick} />
      <div className={classNames.root}>
        {currentStep === 1 && <OnboardingStep
          stepTitle={strings.ManageMembership.labels.step1title}
          stepDescription={strings.ManageMembership.labels.step1description}
          children={
            <SelectDestination
              selectedDestination={selectedDestination}
              onSearchDestinationChange={handleSearchDestinationChange}
            />}
        />}
        {currentStep === 2 && <OnboardingStep
          stepTitle={strings.ManageMembership.labels.step2title}
          stepDescription={strings.ManageMembership.labels.step2description}
          destinationType="Group"
          destinationName={selectedDestination?.name}
          children={
            <AdvancedQuery
              query={query}
            />}
        />}
        <div className={classNames.bottomContainer}>
          {currentStep !== 1 && <div className={classNames.backButtonContainer}>
            <DefaultButton text={strings.back} onClick={onBackStepClick} />
          </div>}
          <div className={classNames.circlesContainer}>
            {Array.from({ length: 4 }, (_, index) => (
              <Icon
                key={index}
                iconName={index === currentStep - 1 ? 'CircleFill' : 'CircleRing'}
                className={classNames.circleIcon}
              />
            ))}
          </div>
          <div className={classNames.nextButtonContainer}>
            <PrimaryButton text={strings.next} onClick={onNextStepClick} disabled={isNextDisabled} />
          </div>
        </div>
      </div >
      <Dialog
        hidden={!showLeaveManageMembershipDialog}
        onDismiss={onDialogClose}
        dialogContentProps={{
          type: DialogType.normal,
          title: strings.ManageMembership.labels.abandonOnboarding,
          subText: strings.ManageMembership.labels.abandonOnboardingDescription
        }}
        modalProps={{
          isBlocking: true,
          styles: { main: { maxWidth: 450 } },
        }}
      >
        <DialogFooter>
          <PrimaryButton onClick={onConfirmExit} text={strings.ManageMembership.labels.confirmAbandon} />
          <DefaultButton onClick={onDialogClose} text={strings.cancel} />
        </DialogFooter>
      </Dialog>
    </Page>
  )
};
