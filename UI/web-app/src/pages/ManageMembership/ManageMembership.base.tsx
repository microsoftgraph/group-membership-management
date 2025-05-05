// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useMemo, useRef, useState } from 'react';
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
  IComboBoxOption,
  IComboBox,
} from '@fluentui/react';
import { useNavigate, useLocation, useParams } from 'react-router-dom';
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
  manageMembershipRequestor,
  manageMembershipAdvancedViewQuery,
  manageMembershipCreatedGroupId,
  setCreatedGroupName,
  manageMembershipCreatedGroupName,
  manageMembershipBusinessJustification,
  setBusinessJustification,
  manageMembershipLastModifiedOnBehalfOfDisplayName,
  manageMembershipLastModifiedOnBehalfOfObjectId,
  setGroupSettings,
  manageMembershipGroupSettings
} from '../../store/manageMembership.slice';
import { getGroupEndpoints, getGroupOnboardingStatus, getChannelOnboardingStatus } from '../../store/manageMembership.api';
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
import { selectIsJobTenantWriter, selectIsJobWriter } from '../../store/roles.slice';
import { PostGroupResponse, SyncStatus } from '../../models';
import { SyncJobQuery } from '../../models/SyncJobQuery';
import { PatchJobRequest } from '../../models/PatchJobRequest';
import { SyncJobChangeReason } from '../../models/SyncJobChangeReason';
import { selectOrgLeaderDataReturned } from '../../store/orgLeaderDetails.slice';
import { createGroup } from '../../store/groups.api';
import { selectIsBusinessJustificationRequired } from '../../store/settings.slice';
import { DestinationType } from '../../models/DestinationType';
import { ChannelOnboardingStatusRequest } from '../../models/ChannelOnboardingStatusRequest';
import { GroupSettings } from '../../models/GroupSettings';

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
  const { jobId: urlJobId } = useParams<{ jobId: string }>();
  const locationState = location.state as { currentStep?: number, jobId?: string };
  const jobId = locationState?.jobId ?? urlJobId;
  const orgLeaderDataReturned = useSelector(selectOrgLeaderDataReturned);

  const dispatch = useDispatch<AppDispatch>();
  useEffect(() => {
    dispatch(setPagingBarVisible(false));
  }, [dispatch]);

  const [showLeaveManageMembershipDialog, setShowLeaveManageMembershipDialog] = useState(false);
  const [isPostingJob, setIsPostingJob] = useState(false);
  const [isEditingJob, setIsEditingJob] = useState(false);
  const currentStep = useSelector(manageMembershipCurrentStep);
  const [isStep1ConditionsMet, setIsStep1ConditionsMet] = useState(false);  
  const hasChanges = useSelector(manageMembershipHasChanges);
  const selectedDestination = useSelector(manageMembershipSelectedDestination);
  const isGroupReadyForOnboarding = useSelector(manageMembershipIsGroupReadyForOnboarding);
  const isJobWriter = useSelector(selectIsJobWriter);

  // Existing job
  const jobDetailsRef = useRef(useSelector(selectSelectedJobDetails));
  const isLoading = useSelector(selectSelectedJobLoading);

  useEffect(() => {
    dispatch(resetManageMembership());
    let editingExistingJob = !!jobId;
    dispatch(setIsEditingExistingJob(editingExistingJob));

    if (!editingExistingJob) {
      jobDetailsRef.current = undefined;
    }

    if (locationState?.currentStep) {
      dispatch(setCurrentStep(locationState.currentStep));
    }

    if (jobId) {
      dispatch(fetchJobDetails({
        syncJobId: jobId
      }));
    } else {
      dispatch(resetManageMembership());
    }
  }, [dispatch, jobId, locationState, location]);

  useEffect(() => {
    if (jobDetailsRef.current) {
      dispatch(setJobDetailsForExistingJob(jobDetailsRef.current));
    }
  }, [dispatch, jobDetailsRef.current]);
  
  useEffect(() => {
    setIsStep1ConditionsMet(!!selectedDestination && isGroupReadyForOnboarding === true);
  }, [selectedDestination, isGroupReadyForOnboarding]);

  const isAdvancedQueryValid = useSelector(manageMembershipisAdvancedQueryValid);
  const allSourcePartsValid = useSelector(areAllSourcePartsValid);
  const startDate = useSelector(manageMembershipStartDate);
  const period = useSelector(manageMembershipPeriod);
  const thresholdPercentageForAdditions = useSelector(manageMembershipThresholdPercentageForAdditions);
  const thresholdPercentageForRemovals = useSelector(manageMembershipThresholdPercentageForRemovals);
  const createdGroupId = useSelector(manageMembershipCreatedGroupId);
  const createdGroupName = useSelector(manageMembershipCreatedGroupName);
  const groupSettings = useSelector(manageMembershipGroupSettings);
  const currentUser = useSelector(selectAccountUsername) ?? '';
  const inputRequestor = useSelector(manageMembershipRequestor);
  const requestor: string = inputRequestor === '' ? currentUser : inputRequestor;
  const lastModifiedOnBehalfOfDisplayName = useSelector(manageMembershipLastModifiedOnBehalfOfDisplayName);
  const lastModifiedOnBehalfOfObjectId = useSelector(manageMembershipLastModifiedOnBehalfOfObjectId);
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);
  const advancedViewQuery = useSelector(manageMembershipAdvancedViewQuery);
  const sourcePartsQuery = useSelector(manageMembershipCompositeQuery);
  const isTenantJobWriter: boolean | undefined = useSelector(selectIsJobTenantWriter);
  const isBusinessJustificationRequired = useSelector(selectIsBusinessJustificationRequired);
  const businessJustification: string = useSelector(manageMembershipBusinessJustification) ?? '';
  const isBusinessJustificationProvided = businessJustification !== '';

  const finalQuery: SyncJobQuery = useMemo(() => {
    if (!sourcePartsQuery || sourcePartsQuery.length === 0) {
      return advancedViewQuery ? JSON.parse(advancedViewQuery) : {} as SyncJobQuery;
    }
    return sourcePartsQuery;
  }, [sourcePartsQuery, advancedViewQuery]);
  
  const handleDestinationTypeChange = (
    event: React.FormEvent<IComboBox>,
    option?: IComboBoxOption
  ): void => {
    dispatch(setHasChanges(true));
    const updatedDestination: Destination = {
      type: option?.key as string,
    };
    dispatch(setSelectedDestination(updatedDestination));
  };

  const handleSearchDestinationChange = (selectedDestinations: IPersonaProps[] | undefined) => {
    dispatch(setHasChanges(true));

    if (selectedDestinations && selectedDestinations.length > 0) {
      const selectedGroupId = selectedDestinations[0].id as string;
      const groupName = selectedDestinations[0].text as string;
      const updatedDestination: Destination = {
        id: selectedGroupId,
        name: groupName,
        type: selectedDestination?.type ?? DestinationType.GroupMembership
      };

      dispatch(setSelectedDestination(updatedDestination));
      dispatch(getGroupEndpoints(selectedGroupId));
      if (updatedDestination.type === DestinationType.GroupMembership) {
        dispatch(getGroupOnboardingStatus(selectedGroupId));
      }
    } else {
      const updatedDestination: Destination = {
        id: undefined,
        name: undefined,
        type: selectedDestination?.type ?? DestinationType.GroupMembership
      };
      dispatch(setSelectedDestination(updatedDestination));
    }
  };

  const handleSearchChannelChange = (selectedChannels: IPersonaProps[] | undefined) => {
    dispatch(setHasChanges(true));
    if (selectedChannels && selectedChannels.length > 0) {
      const selectedChannelId = selectedChannels[0].id as string;
      const channelName = selectedChannels[0].text as string;
      const updatedDestination: Destination = {
        ...selectedDestination,
        type: DestinationType.TeamsChannelMembership,
        channelId: selectedChannelId,
        channelName: channelName,
      };
      dispatch(setSelectedDestination(updatedDestination));
      const channelOnboardingStatusRequest: ChannelOnboardingStatusRequest = {
        teamId: updatedDestination.id!,
        channelId: selectedChannelId,
      };
      dispatch(getChannelOnboardingStatus(channelOnboardingStatusRequest));
    } else {
      const updatedDestination: Destination = {
        ...selectedDestination,
        channelId: undefined,
        channelName: undefined,
        type: selectedDestination?.type ?? DestinationType.GroupMembership
      };
      dispatch(setSelectedDestination(updatedDestination));
    }
  };

  const handleGroupCreated = async (groupName: string, groupAlias: string) => {
    await dispatch(setCreatedGroupName(groupName));
    await dispatch(createGroup({ groupName, groupAlias }));
  };

  useEffect(() => {
    if(createdGroupId && createdGroupName){
      const selectedDestination: Destination = {
        id: createdGroupId,
        name: createdGroupName,
        type: DestinationType.GroupMembership, // Make type configurable once we support Teams Channel creation
        groupSettings: groupSettings
      };
      dispatch(setSelectedDestination(selectedDestination));
      dispatch(getGroupEndpoints(createdGroupId));
    }
  }, [createdGroupId, createdGroupName, groupSettings, dispatch]);

  const handleEditBusinessJustification = (justification: string) => {
    dispatch(setBusinessJustification(justification));
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
    if (jobId !== undefined) {
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
        path: "/LastModifiedOnBehalfOfDisplayName",
        value: lastModifiedOnBehalfOfDisplayName
      },
      {
        op: "replace",
        path: "/LastModifiedOnBehalfOfObjectId",
        value: lastModifiedOnBehalfOfObjectId
      }
    ];

      setIsEditingJob(true);
      const patchRequest: PatchJobRequest = {
        syncJobId: jobId,
        patchOperation,
        changeReason: SyncJobChangeReason.Update,
        businessJustification: businessJustification
      };

      try {
        await dispatch(patchJobDetails(patchRequest));
        dispatch(resetManageMembership());
        dispatch(clearSourceParts());
        navigate('/');
        setIsEditingJob(false);
      } catch (error) {
        console.error("Error editing job:", error);
      }

    } else {
      const destinationJson = JSON.stringify([{
        value: { objectId: selectedDestination?.id, channelId: selectedDestination?.channelId },
        type: selectedDestination?.type
      }]);

      const newJob: NewJob = {
        destination: destinationJson,
        requestor: requestor ?? '',
        lastModifiedOnBehalfOfDisplayName: lastModifiedOnBehalfOfDisplayName ?? '',
        lastModifiedOnBehalfOfObjectId: lastModifiedOnBehalfOfObjectId ?? '',
        startDate: startDate,
        period: period,
        query: finalQuery,
        thresholdPercentageForAdditions: thresholdPercentageForAdditions,
        thresholdPercentageForRemovals: thresholdPercentageForRemovals,
        status: 'Idle',
        businessJustification: businessJustification,
        groupSettings: groupSettings,
      };

      setIsPostingJob(true);

      try {
        await dispatch(postJob(newJob));
        dispatch(resetManageMembership());
        dispatch(clearSourceParts());
        navigate('/');
        setIsPostingJob(false);
      } catch (error) {
        console.error("Error posting job:", error);
      }
    }
  };

  const isStep3ConditionsMet = isAdvancedQueryValid || allSourcePartsValid;
  let isNextDisabled = false;

  if (currentStep === OnboardingSteps.SelectDestination && !isStep1ConditionsMet) {
    isNextDisabled = true;
  } else if (currentStep === OnboardingSteps.RunConfiguration) {
    isNextDisabled = false;
  } else if (currentStep === OnboardingSteps.MembershipConfiguration && !isStep3ConditionsMet) {
    isNextDisabled = true;
  } else if (currentStep === OnboardingSteps.Confirmation || !isJobWriter) {
    isNextDisabled = true;
  }
  if (orgLeaderDataReturned === false) {
    isNextDisabled = true;
  }

  const isSubmitDisabled = isBusinessJustificationRequired && !isBusinessJustificationProvided;

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
                onDestinationTypeChange={handleDestinationTypeChange}
                onSearchDestinationChange={handleSearchDestinationChange}
                onSearchChannelChange={handleSearchChannelChange}
                onGroupCreated={handleGroupCreated}
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
              <MembershipConfiguration isEditable={true} />
            }
          />}
          {currentStep === OnboardingSteps.Confirmation && <OnboardingStep
            stepTitle={strings.ManageMembership.labels.step4title}
            stepDescription={strings.ManageMembership.labels.step4description}
            destinationType={selectedDestination?.type ?? jobDetailsRef.current?.targetGroupType}
            destinationName={selectedDestination?.name ?? jobDetailsRef.current?.targetGroupName}
            children={
              <Confirmation
                onEditButtonClick={onEditButtonClick}
                onEditBusinessJustification={handleEditBusinessJustification}
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
                <PrimaryButton text={strings.submit} onClick={handleSaveButtonClick} disabled={isSubmitDisabled} />
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
