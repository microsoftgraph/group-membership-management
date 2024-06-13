// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useRef, useState } from 'react';
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
import { useNavigate, useLocation } from 'react-router-dom';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { IManageMembershipProps, IManageMembershipStyleProps, IManageMembershipStyles } from './ManageMembership.types';
import { AppDispatch } from '../../store';
import { useStrings } from '../../store/hooks';
import { OnboardingStep } from '../../components/OnboardingStep';
import { SelectDestination } from '../../components/SelectDestination';
import { Destination } from '../../models/Destination';
import {
  manageMembershipIsGroupReadyForOnboarding,
  manageMembershipCurrentStep,
  manageMembershipHasChanges,
  manageMembershipisAdvancedQueryValid,
  manageMembershipQuery,
  manageMembershipSelectedDestination,
  setCurrentStep,
  setHasChanges,
  setSelectedDestination,
  manageMembershipStartDate,
  manageMembershipPeriod,
  manageMembershipThresholdPercentageForAdditions,
  manageMembershipThresholdPercentageForRemovals,
  resetManageMembership,
  areAllSourcePartsValid,
  setJobDetailsForExistingJob,
  setIsEditingExistingJob,
  manageMembershipIsEditingExistingJob,
  manageMembershipCompositeQuery,
  clearSourceParts,
  manageMembershipRequestor
} from '../../store/manageMembership.slice';
import { getGroupEndpoints, getGroupOnboardingStatus } from '../../store/manageMembership.api';
import { NewJob } from '../../models/NewJob';
import { fetchJobs, postJob } from '../../store/jobs.api';
import { RunConfiguration } from '../../components/RunConfiguration';
import { Confirmation } from '../../components/Confirmation';
import { selectAccountUsername } from '../../store/account.slice';
import { setPagingBarVisible } from '../../store/pagingBar.slice';
import { MembershipConfiguration } from '../../components/MembershipConfiguration';
import { OnboardingSteps } from '../../models/OnboardingSteps';
import { selectSelectedJobDetails, selectSelectedJobLoading } from '../../store/jobs.slice';
import { fetchJobDetails, patchJobDetails } from '../../store/jobDetails.api';
import { Loader } from '../../components/Loader';
import { selectIsJobWriter } from '../../store/roles.slice';
import { SyncStatus } from '../../models';
import { SyncJobQuery } from '../../models/SyncJobQuery';

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
  const location = useLocation();
  const locationState = location.state as { currentStep?: number, jobId?: string };

  const dispatch = useDispatch<AppDispatch>();
  useEffect(() => {
    dispatch(setPagingBarVisible(false));
  }, [dispatch]);

  const [showLeaveManageMembershipDialog, setShowLeaveManageMembershipDialog] = useState(false);
  const [isPostingJob, setIsPostingJob] = useState(false);
  const [isEditingJob, setIsEditingJob] = useState(false);
  const currentStep = useSelector(manageMembershipCurrentStep);
  const hasChanges = useSelector(manageMembershipHasChanges);
  const selectedDestination = useSelector(manageMembershipSelectedDestination);
  const isGroupReadyForOnboarding = useSelector(manageMembershipIsGroupReadyForOnboarding);
  const isJobWriter = useSelector(selectIsJobWriter)

  // Existing job
  const jobDetailsRef = useRef(useSelector(selectSelectedJobDetails));
  const isLoading = useSelector(selectSelectedJobLoading);

  useEffect(() => {
    let editingExistingJob = !!locationState?.jobId && isJobWriter;
    dispatch(setIsEditingExistingJob(editingExistingJob));

    if (!editingExistingJob) {
      jobDetailsRef.current = undefined;
    }

    if (locationState?.currentStep) {
      dispatch(setCurrentStep(locationState.currentStep));
    }

    if (locationState?.jobId) {
      dispatch(fetchJobDetails({
        syncJobId: locationState.jobId
      }));
    } else {
      dispatch(resetManageMembership());
    }
  }, [dispatch, locationState, isJobWriter]);

  useEffect(() => {
    if (jobDetailsRef.current) {
      dispatch(setJobDetailsForExistingJob(jobDetailsRef.current));
    }
  }, [dispatch, jobDetailsRef.current]);

  const isAdvancedQueryValid = useSelector(manageMembershipisAdvancedQueryValid);
  const allSourcePartsValid = useSelector(areAllSourcePartsValid);
  const startDate = useSelector(manageMembershipStartDate);
  const period = useSelector(manageMembershipPeriod);
  const thresholdPercentageForAdditions = useSelector(manageMembershipThresholdPercentageForAdditions);
  const thresholdPercentageForRemovals = useSelector(manageMembershipThresholdPercentageForRemovals);
  const currentUser = useSelector(selectAccountUsername) ?? '';
  const inputRequestor = useSelector(manageMembershipRequestor);
  const requestor: string = inputRequestor === '' ? currentUser : inputRequestor;
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);
  const advancedViewQuery = useSelector(manageMembershipQuery);
  const sourcePartsQuery = useSelector(manageMembershipCompositeQuery);

  let finalQuery: SyncJobQuery;
  if(!sourcePartsQuery || sourcePartsQuery.length === 0) {
    finalQuery = advancedViewQuery;
  } else {
    finalQuery = sourcePartsQuery;
  }
  
  const handleSearchDestinationChange = (selectedDestinations: IPersonaProps[] | undefined) => {
    dispatch(setHasChanges(true));

    if (selectedDestinations && selectedDestinations.length > 0) {
      const selectedGroupId = selectedDestinations[0].id as string;
      const groupName = selectedDestinations[0].text as string;
      const selectedDestination: Destination = {
        id: selectedGroupId,
        name: groupName,
        type: 'GroupMembership' // Make type configurable once we support Teams Channels
      };

      dispatch(setSelectedDestination(selectedDestination));
      dispatch(getGroupEndpoints(selectedGroupId));
      dispatch(getGroupOnboardingStatus(selectedGroupId));
    } else {
      dispatch(setSelectedDestination(undefined));
    }
  };

  const handleBackToDashboardButtonClick = () => {
    if (hasChanges){
      setShowLeaveManageMembershipDialog(true);
    }
    else {
      navigate('/');
      dispatch(resetManageMembership());
    }
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
    navigate('/');
    setShowLeaveManageMembershipDialog(false);
    dispatch(resetManageMembership());
  };

  const handleSaveButtonClick = async () => {
    if (locationState.jobId !== undefined) {
      const patchOperation = [{
        op: "replace",
        path: "/Query",
        value: JSON.stringify(finalQuery)
      },
      {
        op: "replace",
        path: "/Status",
        value: SyncStatus.PendingReview
      },
      {
        op: "replace",
        path: "/StartDate",
        value: startDate
      },
      {
        op: "replace",
        path: "/Period",
        value: period
      },
      {
        op: "replace",
        path: "/ThresholdPercentageForAdditions",
        value: thresholdPercentageForAdditions
      },
      {
        op: "replace",
        path: "/ThresholdPercentageForRemovals",
        value: thresholdPercentageForRemovals
      },
      {
        op: "replace",
        path: "/Requestor",
        value: requestor
      }
    ];

      setIsEditingJob(true);

      try {
        await dispatch(patchJobDetails({syncJobId: locationState.jobId, patchOperation: patchOperation}));
        dispatch(resetManageMembership());
        dispatch(clearSourceParts());
        await dispatch(fetchJobs());
        navigate('/');
        setIsEditingJob(false);
      } catch (error) {
        console.error("Error editing job:", error);
      }

    } else {
      const destinationJson = JSON.stringify([{
        value: { objectId: selectedDestination?.id },
        type: selectedDestination?.type
      }]);

      const newJob: NewJob = {
        destination: destinationJson,
        requestor: requestor ?? '',
        startDate: startDate,
        period: period,
        query: finalQuery,
        thresholdPercentageForAdditions: thresholdPercentageForAdditions,
        thresholdPercentageForRemovals: thresholdPercentageForRemovals,
        status: 'Idle',
      };

      setIsPostingJob(true);

      try {
        await dispatch(postJob(newJob));
        dispatch(resetManageMembership());
        dispatch(clearSourceParts());
        await dispatch(fetchJobs());
        navigate('/');
        setIsPostingJob(false);
      } catch (error) {
        console.error("Error posting job:", error);
      }
    }
  };

  const isStep1ConditionsMet = selectedDestination && isGroupReadyForOnboarding === true;
  const isStep3ConditionsMet = isAdvancedQueryValid || allSourcePartsValid;
  let isNextDisabled = false;

  if (currentStep === OnboardingSteps.SelectDestination && !isStep1ConditionsMet) {
    isNextDisabled = true;
  } else if (currentStep === OnboardingSteps.RunConfiguration) {
    isNextDisabled = false;
  } else if (currentStep === OnboardingSteps.MembershipConfiguration && !isStep3ConditionsMet) {
    isNextDisabled = true;
  } else if (currentStep === OnboardingSteps.Confirmation) {
    isNextDisabled = true;
  }

  return (
    <Page>
      <PageHeader onBackToDashboardButtonClick={isEditingExistingJob ? undefined : handleBackToDashboardButtonClick} />
      {isLoading ?
        <Loader /> :
        <div className={classNames.root}>
          {currentStep === OnboardingSteps.SelectDestination && <OnboardingStep
            stepTitle={strings.ManageMembership.labels.step1title}
            stepDescription={strings.ManageMembership.labels.step1description}
            children={
              <SelectDestination
                selectedDestination={selectedDestination}
                onSearchDestinationChange={handleSearchDestinationChange}
              />}
          />}
          {currentStep === OnboardingSteps.RunConfiguration && <OnboardingStep
            stepTitle={strings.ManageMembership.labels.step2title}
            stepDescription={strings.ManageMembership.labels.step2description}
            destinationType={selectedDestination?.type}
            destinationName={selectedDestination?.name}
            children={
              <RunConfiguration />}
          />}
          {currentStep === OnboardingSteps.MembershipConfiguration && <OnboardingStep
            stepTitle={strings.ManageMembership.labels.step3title}
            stepDescription={strings.ManageMembership.labels.step3description}
            destinationType={selectedDestination?.type}
            destinationName={selectedDestination?.name}
            children={
              <MembershipConfiguration />
            }
          />}
          {currentStep === OnboardingSteps.Confirmation && <OnboardingStep
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
            {currentStep !== OnboardingSteps.SelectDestination && <div className={classNames.backButtonContainer}>
              {!(isEditingExistingJob && currentStep === OnboardingSteps.RunConfiguration) &&
                <DefaultButton
                  text={strings.back}
                  onClick={onBackStepClick}
                />}
            </div>}
            <div className={classNames.circlesContainer}>
              {Array.from({ length: 4 }, (_, index) => (
                <Icon
                  key={index}
                  iconName={index === currentStep ? 'CircleFill' : 'CircleRing'}
                  className={classNames.circleIcon}
                />
              ))}
            </div>
            <div className={classNames.nextButtonContainer}>
              {currentStep === OnboardingSteps.Confirmation ?
                <PrimaryButton text={strings.submit} onClick={handleSaveButtonClick} />
                : <PrimaryButton text={strings.next} onClick={onNextStepClick} disabled={isNextDisabled} />}
            </div>
          </div>
        </div >
      }
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
      {(isPostingJob || isEditingJob) && (
        <div className={classNames.overlay}>
          <Spinner label={isPostingJob ? strings.ManageMembership.labels.savingSyncJob : strings.ManageMembership.labels.updatingSyncJob} ariaLive="assertive" labelPosition="right" />
        </div>
      )}
    </Page>
  )
};
