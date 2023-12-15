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
  IPersonaProps,
  Dialog, DialogType, DialogFooter,
  Spinner,
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
  setSelectedDestination,
  manageMembershipStartDate,
  manageMembershipPeriod,
  manageMembershipThresholdPercentageForAdditions,
  manageMembershipThresholdPercentageForRemovals,
  resetManageMembership
} from '../../store/manageMembership.slice';
import { getGroupEndpoints, getGroupOnboardingStatus } from '../../store/manageMembership.api';
import { NewJob } from '../../models/NewJob';
import { fetchJobs, postJob } from '../../store/jobs.api';
import { RunConfiguration } from '../../components/RunConfiguration';
import { Confirmation } from '../../components/Confirmation';
import { selectAccountUsername } from '../../store/account.slice';

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
  const [isPostingJob, setIsPostingJob] = useState(false);

  const currentStep = useSelector(manageMembershipCurrentStep);
  const hasChanges = useSelector(manageMembershipHasChanges);
  const selectedDestination = useSelector(manageMembershipSelectedDestination);
  const isGroupReadyForOnboarding = useSelector(manageMembershipIsGroupReadyForOnboarding);

  // Onboarding values
  const query = useSelector(manageMembershipQuery);
  const isQueryValid = useSelector(manageMembershipIsQueryValid);
  const startDate = useSelector(manageMembershipStartDate);
  const period = useSelector(manageMembershipPeriod);
  const thresholdPercentageForAdditions = useSelector(manageMembershipThresholdPercentageForAdditions);
  const thresholdPercentageForRemovals = useSelector(manageMembershipThresholdPercentageForRemovals);
  const requestor = useSelector(selectAccountUsername);

  const handleSearchDestinationChange = (selectedDestinations: IPersonaProps[] | undefined) => {
    if (selectedDestinations && selectedDestinations.length > 0) {
      dispatch(setHasChanges(true));
      
      const selectedGroupId = selectedDestinations[0].key as string;
      const groupName = selectedDestinations[0].text as string;
  
      const selectedDestination: Destination = {
        id: selectedGroupId,
        name: groupName,
        type: 'GroupMembership' // Make type configurable once we support Teams Channels
      };
  
      dispatch(setSelectedDestination(selectedDestination));
      dispatch(getGroupEndpoints(selectedGroupId));
      dispatch(getGroupOnboardingStatus(selectedGroupId));
    }
  };

  const handleBackToDashboardButtonClick = () => {
    if (hasChanges)
      setShowLeaveManageMembershipDialog(true);
  };

  const onEditButtonClick = (stepToEdit: number) => {
    dispatch(setCurrentStep(stepToEdit));
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
    dispatch(resetManageMembership());
  };

  const handleSaveButtonClick = async () => {
    const destinationJson = JSON.stringify([{
      value: { objectId: selectedDestination?.id },
      type: selectedDestination?.type
    }]);

    const newJob: NewJob = {
      destination: destinationJson,
      requestor: requestor ?? '',
      startDate: startDate,
      period: period,
      query: query,
      thresholdPercentageForAdditions: thresholdPercentageForAdditions,
      thresholdPercentageForRemovals: thresholdPercentageForRemovals,
      status: 'Idle',
    };

    setIsPostingJob(true);

    try {
      await dispatch(postJob(newJob));
      await dispatch(fetchJobs());
      navigate(-1);
      setIsPostingJob(false);
    } catch (error) {
      console.error("Error posting job:", error);
    }
  };

  const isStep1ConditionsMet = selectedDestination && isGroupReadyForOnboarding === true;
  const isStep3ConditionsMet = isQueryValid;
  let isNextDisabled = false;

  if (currentStep === 1 && !isStep1ConditionsMet) {
    isNextDisabled = true;
  } else if (currentStep === 2) {
    isNextDisabled = false;
  } else if (currentStep === 3 && !isStep3ConditionsMet) {
    isNextDisabled = true;
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
          destinationType={selectedDestination?.type}
          destinationName={selectedDestination?.name}
          children={
            <RunConfiguration />}
        />}
        {currentStep === 3 && <OnboardingStep
          stepTitle={strings.ManageMembership.labels.step3title}
          stepDescription={strings.ManageMembership.labels.step3description}
          destinationType={selectedDestination?.type}
          destinationName={selectedDestination?.name}
          children={
            <AdvancedQuery
              query={query}
            />}
        />}
        {currentStep === 4 && <OnboardingStep
          stepTitle={strings.ManageMembership.labels.step4title}
          stepDescription={strings.ManageMembership.labels.step4description}
          destinationType={selectedDestination?.type}
          destinationName={selectedDestination?.name}
          children={
            <Confirmation
              onEditButtonClick={onEditButtonClick}
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
            {currentStep === 4 ?
              <PrimaryButton text={strings.submit} onClick={handleSaveButtonClick} />
              : <PrimaryButton text={strings.next} onClick={onNextStepClick} disabled={isNextDisabled} />}
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
      {isPostingJob && (
        <div className={classNames.overlay}>
          <Spinner label={strings.ManageMembership.labels.savingSyncJob} ariaLive="assertive" labelPosition="right" />
        </div>
      )}
    </Page>
  )
};
