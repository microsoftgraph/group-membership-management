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
  manageMembershipIsMissingAndOrOperator,
  manageMembershipisAdvancedQueryValid,
  manageMembershipIsAdvancedView,
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
  manageMembershipGroupSettings,
  getSourcePartsFromState,
  manageMembershipGroupMembers
} from '../../store/manageMembership.slice';
import { getGroupEndpoints, getGroupOnboardingStatus, getChannelOnboardingStatus, getGroupMembers } from '../../store/manageMembership.api';
import { clearGroupMembers } from '../../store/manageMembership.slice';
import { NewJob } from '../../models/NewJob';
import { fetchJobs, postJob } from '../../store/jobs.api';
import { selectTitles } from '../../store/title.slice';
import { allSourcePartsHaveFreshTitles } from '../../utils/titleFreshness';
import { RunConfiguration } from '../../components/RunConfiguration';
import { Confirmation } from '../../components/Confirmation';
import { selectAccountUsername } from '../../store/account.slice';
import { setPagingBarVisible } from '../../store/pagingBar.slice';
import { MembershipConfiguration } from '../../components/MembershipConfiguration';
import { OnboardingSteps } from '../../models/OnboardingSteps';
import { selectSelectedJobDetails, selectSelectedJobLoading, selectSelectedJobWithNoTitles } from '../../store/jobs.slice';
import { fetchJobDetails, patchJobDetails } from '../../store/jobDetails.api';
import { Loader } from '../../components/Loader';
import { selectIsJobTenantWriter, selectIsJobWriter, selectIsAIOnboardingChat } from '../../store/roles.slice';
import { PostGroupResponse, SyncStatus } from '../../models';
import { SyncJobQuery } from '../../models/SyncJobQuery';
import { PatchJobRequest } from '../../models/PatchJobRequest';
import { SyncJobChangeReason } from '../../models/SyncJobChangeReason';
import { selectOrgLeaderDataReturned } from '../../store/orgLeaderDetails.slice';
import { createGroup } from '../../store/groups.api';
import { selectIsBusinessJustificationRequired, selectIsAICopilotEnabled } from '../../store/settings.slice';
import { DestinationType } from '../../models/DestinationType';
import { ChannelOnboardingStatusRequest } from '../../models/ChannelOnboardingStatusRequest';
import { GroupSettings } from '../../models/GroupSettings';
import { openPanel, selectIsPanelOpen, selectCopilotMessages } from '../../store/copilot.slice';
import { mergeStyles, keyframes } from '@fluentui/react';

const sparkleAnimation1 = keyframes({
  '0%, 100%': { opacity: 1, transform: 'scale(1)' },
  '50%': { opacity: 0.2, transform: 'scale(0.5)' },
});

const sparkleAnimation2 = keyframes({
  '0%, 100%': { opacity: 0.3, transform: 'scale(0.5)' },
  '50%': { opacity: 1, transform: 'scale(1)' },
});

const sparkleClass1 = mergeStyles({
  animationName: sparkleAnimation1,
  animationDuration: '2s',
  animationTimingFunction: 'ease-in-out',
  animationIterationCount: 'infinite',
  transformOrigin: 'center',
  transformBox: 'fill-box',
});

const sparkleClass2 = mergeStyles({
  animationName: sparkleAnimation2,
  animationDuration: '2s',
  animationTimingFunction: 'ease-in-out',
  animationIterationCount: 'infinite',
  transformOrigin: 'center',
  transformBox: 'fill-box',
});

const getCopilotButtonClass = (theme: ReturnType<typeof useTheme>) => mergeStyles({
  display: 'flex',
  alignItems: 'center',
  gap: '10px',
  borderRadius: '28px',
  padding: '6px 16px 6px 6px',
  border: `1px solid ${theme.palette.neutralLight}`,
  cursor: 'pointer',
  transition: 'box-shadow 0.2s ease, border-color 0.2s ease',
  background: theme.palette.white,
  selectors: {
    ':hover': {
      boxShadow: '0 2px 6px rgba(0, 0, 0, 0.12)',
      borderColor: theme.palette.neutralTertiaryAlt,
    },
    ':disabled': {
      opacity: 0.5,
      cursor: 'not-allowed',
    },
  },
});

const CopilotTriggerButton: React.FunctionComponent = () => {
  const dispatch = useDispatch<AppDispatch>();
  const strings = useStrings();
  const theme = useTheme();
  const isJobWriter = useSelector(selectIsJobWriter);
  const isAIOnboardingChat = useSelector(selectIsAIOnboardingChat);
  const isAICopilotEnabled = useSelector(selectIsAICopilotEnabled);
  const hasCopilotHistory = useSelector(selectCopilotMessages).length > 0;

  if (!isAIOnboardingChat || isAICopilotEnabled === false) return null;

  return (
    <button
      onClick={() => dispatch(openPanel())}
      disabled={!isJobWriter}
      className={getCopilotButtonClass(theme)}
    >
      <div style={{
        width: '34px',
        height: '34px',
        borderRadius: '50%',
        backgroundColor: theme.palette.themePrimary,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        flexShrink: 0,
        position: 'relative',
      }}>
        <Icon iconName="Contact" style={{ color: theme.palette.white, fontSize: '15px' }} />
        <svg
          width="16" height="16"
          viewBox="0 0 16 16"
          fill="none"
          style={{ position: 'absolute', top: '-6px', right: '-6px' }}
        >
          {/* Larger 4-pointed star */}
          <path className={sparkleClass1} d="M8 4 L9 7 L12 8 L9 9 L8 12 L7 9 L4 8 L7 7 Z" fill={theme.palette.themePrimary} />
          {/* Smaller 4-pointed star */}
          <path className={sparkleClass2} d="M13 1 L13.5 2.5 L15 3 L13.5 3.5 L13 5 L12.5 3.5 L11 3 L12.5 2.5 Z" fill={theme.palette.themePrimary} />
        </svg>
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', textAlign: 'left' }}>
        <span style={{ color: theme.palette.neutralPrimary, fontWeight: 600, fontSize: '13px', lineHeight: '18px' }}>
          {strings.Copilot?.title || 'GMM Copilot'}
        </span>
        <span style={{ color: theme.palette.neutralSecondary, fontSize: '11px', fontWeight: 400, lineHeight: '16px' }}>
          {hasCopilotHistory
            ? (strings.Copilot?.triggerButtonResume || 'Let GMM resume building for you')
            : (strings.Copilot?.triggerButton || 'Let GMM build it for you')}
        </span>
      </div>
    </button>
  );
};

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
  const locationState = location.state as { currentStep?: number, jobId?: string, thresholdExceededForAdditions?: boolean, thresholdExceededForRemovals?: boolean };
  const jobId = locationState?.jobId ?? urlJobId;
  const orgLeaderDataReturned = useSelector(selectOrgLeaderDataReturned);

  const dispatch = useDispatch<AppDispatch>();
  useEffect(() => {
    dispatch(setPagingBarVisible(false));
  }, [dispatch]);

  const [showLeaveManageMembershipDialog, setShowLeaveManageMembershipDialog] = useState(false);
  const [isPostingJob, setIsPostingJob] = useState(false);
  const [isEditingJob, setIsEditingJob] = useState(false);
  const [showEditErrorDialog, setShowEditErrorDialog] = useState(false);
  const [editErrorMessage, setEditErrorMessage] = useState('');
  const currentStep = useSelector(manageMembershipCurrentStep);
  const [isStep1ConditionsMet, setIsStep1ConditionsMet] = useState(false);
  const hasChanges = useSelector(manageMembershipHasChanges);
  const selectedDestination = useSelector(manageMembershipSelectedDestination);
  const isGroupReadyForOnboarding = useSelector(manageMembershipIsGroupReadyForOnboarding);
  const isJobWriter = useSelector(selectIsJobWriter);
  // Existing job
  const jobDetailsRef = useRef(useSelector(selectSelectedJobDetails));
  const isLoading = useSelector(selectSelectedJobLoading);
  const jobWithNoTitles = useSelector(selectSelectedJobWithNoTitles);

  useEffect(() => {
    const editingExistingJob = !!jobId;
    dispatch(setIsEditingExistingJob(editingExistingJob));
    if (!editingExistingJob) {
      jobDetailsRef.current = undefined;
      dispatch(resetManageMembership());
    }

    if (locationState?.currentStep) {
      dispatch(setCurrentStep(locationState.currentStep));
    }
    if (jobId) {
      dispatch(fetchJobDetails({
        syncJobId: jobId
      }));
    }
  }, [dispatch, jobId, locationState, location]);

  useEffect(() => {
    if (jobDetailsRef.current) {
      dispatch(setJobDetailsForExistingJob(jobDetailsRef.current));
    }
  }, [dispatch, jobDetailsRef.current]);

  const groupMembers = useSelector(manageMembershipGroupMembers);
  const hasNestedGroups = groupMembers && groupMembers.groupMemberCount > 0;

  useEffect(() => {
    setIsStep1ConditionsMet(!!selectedDestination && isGroupReadyForOnboarding === true && !hasNestedGroups);
  }, [selectedDestination, isGroupReadyForOnboarding, hasNestedGroups]);

  // Reset isEditingExistingJob when leaving ManageMembership page
  useEffect(() => {
    return () => {
      dispatch(setIsEditingExistingJob(false));
      dispatch(resetManageMembership());
    };
  }, [dispatch]);

  const isMissingAndOrOperator = useSelector(manageMembershipIsMissingAndOrOperator);
  const isAdvancedQueryValid = useSelector(manageMembershipisAdvancedQueryValid);
  const isAdvancedView = useSelector(manageMembershipIsAdvancedView);
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
  const sourceParts = useSelector(getSourcePartsFromState);
  const aiGeneratedTitles = useSelector(selectTitles);

  const finalQuery: SyncJobQuery = useMemo(() => {
    // If we have source parts (regular view derived query), prefer that.
    if (sourcePartsQuery && sourcePartsQuery.length > 0) {
      return sourcePartsQuery;
    }
    // Otherwise attempt to parse advanced view text only if it's been validated as JSON.
    if (advancedViewQuery && advancedViewQuery.trim().length > 0 && isAdvancedQueryValid) {
      try {
        const parsed = JSON.parse(advancedViewQuery);
        return Array.isArray(parsed) ? parsed : [];
      } catch {
        // Swallow parse errors – treat as empty until user fixes JSON.
        return [];
      }
    }
    return [];
  }, [sourcePartsQuery, advancedViewQuery, isAdvancedQueryValid]);

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
        dispatch(getGroupMembers(selectedGroupId));
      }
    } else {
      const updatedDestination: Destination = {
        id: undefined,
        name: undefined,
        type: selectedDestination?.type ?? DestinationType.GroupMembership
      };
      dispatch(setSelectedDestination(updatedDestination));
      dispatch(clearGroupMembers());
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
    const allHaveTitles = allSourcePartsHaveFreshTitles(sourceParts, aiGeneratedTitles);
    if (jobId !== undefined) {
      const patchOperation = [];
      patchOperation.push({
        op: "replace",
        path: "/Titles",
        value: allHaveTitles
          ? sourceParts.map(part => ({
              partId: part.id,
              name: part.title
            }))
          : ""
      });
      patchOperation.push(
        {
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
      );

      setIsEditingJob(true);
      const patchRequest: PatchJobRequest = {
        syncJobId: jobId,
        patchOperation,
        changeReason: SyncJobChangeReason.Update,
        businessJustification: businessJustification
      };

      try {
        const result = await dispatch(patchJobDetails(patchRequest)).unwrap();
        if (!result.ok) {
          setIsEditingJob(false);
          const message = result.errorCode === 'JobInProgress'
            ? strings.ManageMembership.labels.jobInProgressDialogMessage
            : strings.ManageMembership.labels.editErrorDialogMessage;
          setEditErrorMessage(message);
          setShowEditErrorDialog(true);
          return;
        }
        dispatch(resetManageMembership());
        dispatch(clearSourceParts());
        navigate('/');
        setIsEditingJob(false);
      } catch (error) {
        setIsEditingJob(false);
        setEditErrorMessage(strings.ManageMembership.labels.editErrorDialogMessage);
        setShowEditErrorDialog(true);
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
        onboardedUsingAIQB: sourceParts.some(sp => sp.createdViaAIQB === true),
        ...(allHaveTitles && {
          titles: sourceParts.map(part => ({
            partId: part.id,
            name: part.title
          }))
        })
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

  // In advanced view, we require the advanced query itself to be valid (ignore sourceParts validity).
  // In regular view, rely solely on the composed source parts validation.
  const isMembershipConfigurationConditionsMet = (
    isAdvancedView ? isAdvancedQueryValid : allSourcePartsValid
  ) && !isMissingAndOrOperator;
  let isNextDisabled = false;

  if (currentStep === OnboardingSteps.SelectDestination && !isStep1ConditionsMet) {
    isNextDisabled = true;
  } else if (currentStep === OnboardingSteps.RunConfiguration) {
    isNextDisabled = false;
  } else if (currentStep === OnboardingSteps.MembershipConfiguration && !isMembershipConfigurationConditionsMet) {
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
          {currentStep === OnboardingSteps.MembershipConfiguration && <OnboardingStep
            stepTitle={strings.ManageMembership.labels.step2title}
            stepDescription={strings.ManageMembership.labels.step2description}
            destinationType={selectedDestination?.type}
            destinationName={selectedDestination?.name}
            headerAction={
              <CopilotTriggerButton />
            }
            children={
              <MembershipConfiguration isEditable={true} />
            }
          />}
          {currentStep === OnboardingSteps.RunConfiguration && <OnboardingStep
            stepTitle={strings.ManageMembership.labels.step3title}
            stepDescription={strings.ManageMembership.labels.step3description}
            destinationType={selectedDestination?.type}
            destinationName={selectedDestination?.name}
            children={
              <RunConfiguration
                thresholdExceededForAdditions={locationState?.thresholdExceededForAdditions}
                thresholdExceededForRemovals={locationState?.thresholdExceededForRemovals}
              />}
          />}
          {currentStep === OnboardingSteps.Confirmation && <OnboardingStep
            stepTitle={strings.ManageMembership.labels.step4title}
            stepDescription={strings.ManageMembership.labels.step4description}
            destinationType={selectedDestination?.type ?? jobDetailsRef.current?.targetDestinationType}
            destinationName={selectedDestination?.name ?? jobDetailsRef.current?.targetGroupName}
            children={
              <Confirmation
                onEditButtonClick={onEditButtonClick}
                onEditBusinessJustification={handleEditBusinessJustification}
              />}
          />}
          <div className={classNames.bottomContainer}>
            {currentStep !== OnboardingSteps.SelectDestination && <div className={classNames.backButtonContainer}>
              {!(isEditingExistingJob && currentStep === OnboardingSteps.MembershipConfiguration) &&
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
      <Dialog
        hidden={!showEditErrorDialog}
        onDismiss={() => setShowEditErrorDialog(false)}
        dialogContentProps={{
          type: DialogType.normal,
          title: strings.ManageMembership.labels.editErrorDialogTitle,
          subText: editErrorMessage
        }}
        modalProps={{
          isBlocking: true,
          styles: { main: { maxWidth: 450 } },
        }}
      >
        <DialogFooter>
          <PrimaryButton onClick={() => setShowEditErrorDialog(false)} text={strings.close} />
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
