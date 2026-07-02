// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  Text,
  Shimmer,
  MessageBar,
  MessageBarType,
  IIconProps,
  Toggle,
  IProcessedStyleSet,
  classNamesFunction,
  ActionButton,
  DefaultButton,
  PrimaryButton,
  Icon,
  Dialog,
  DialogType,
  DialogFooter,
  Persona,
  PersonaSize,
  IPersonaSharedProps,
  Label,
  TextField,
  Spinner,
  SpinnerSize,
  Checkbox
} from '@fluentui/react';

import {
  Stack,
  type IStackTokens
} from '@fluentui/react/lib/Stack';
import React, { useEffect, useState } from 'react';
import { useSelector, useDispatch } from 'react-redux';
import { useNavigate, useParams } from 'react-router-dom';
import { InfoLabel } from '../../components/InfoLabel';
import { PageHeader } from '../../components/PageHeader';
import { type Job } from '../../models/Job';
import { type AppDispatch } from '../../store';
import { fetchJobChanges, fetchJobDetails, fetchSyncNowUsage, getChannelDetails, getGroupDetails, patchJobDetails, removeGMM } from '../../store/jobDetails.api';
import {
  selectSelectedJobDetails,
  setGetJobDetailsError,
  selectGetJobDetailsError,
  selectPatchJobDetailsResponse,
  selectPatchJobDetailsError,
  selectRemoveGMMError,
  selectRemoveGMMLoading,
  selectSelectedJobLoading,
  selectSelectedJobChanges,
  setGeneratedTitlesYet,
  setJobId,
  selectJobIdSet,
  setSelectedJobEnabled,
  setSelectedJobStatus,
  removeJobFromList
} from '../../store/jobs.slice';

import { ContentContainer } from '../../components/ContentContainer/ContentContainer'
import { useTheme } from '@fluentui/react/lib/Theme';
import { Page } from '../../components/Page';
import { format } from 'react-string-format';
import {
  type IJobDetailsProps,
  type IJobDetailsStyleProps,
  type IJobDetailsStyles,
  type IContentProps,
  type IStatusContentProps,
} from './JobDetails.types';
import { useStrings } from '../../store/hooks';
import { setPagingBarVisible } from '../../store/pagingBar.slice';
import { selectIsJobOwnerDeleter, selectIsJobOwnerEnabler, selectIsJobWriter, selectIsSubmissionReviewer, selectIsSubmissionRejector } from '../../store/roles.slice';
import { PatchJobResponse, SyncJobChange, SyncStatus } from '../../models';
import { OnboardingSteps } from '../../models/OnboardingSteps';
import { Loader } from '../../components/Loader';
import { clearSourceParts, manageMembershipBusinessJustification, setIsEditingExistingJob } from '../../store/manageMembership.slice';
import { SyncJobChangeReason } from '../../models/SyncJobChangeReason';
import { PatchJobRequest } from '../../models/PatchJobRequest';
import { MembershipConfiguration } from '../../components/MembershipConfiguration';
import { JobHistoryPanel } from '../../components/JobHistoryPanel/JobHistoryPanel';
import { EndpointsList } from '../../components/EndpointsList';
import { getProfilePhotoUsingId } from '../../store/profile.api';
import { selectLastModifiedOnBehalfOfUserProfile, selectLastModifiedUserProfile } from '../../store/profile.slice';
import { DestinationType } from '../../models/DestinationType';
import { destinationTypeLocalization } from '../../utils/destinationTypeUtils';
import { clearGeneratedGroupParts, clearGeneratedHRParts, clearTitles } from '../../store/title.slice';

const getClassNames = classNamesFunction<
  IJobDetailsStyleProps,
  IJobDetailsStyles
>();

export const JobDetailsBase: React.FunctionComponent<IJobDetailsProps> = (
  props: IJobDetailsProps
) => {
  const strings = useStrings();
  const { className, styles } = props;
  const classNames: IProcessedStyleSet<IJobDetailsStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  // Source of truth for the displayed job is Redux. Do NOT fall back to
  // `location.state?.item` from React Router — that is the row payload from
  // the JobsList click and goes stale on rapid renavigation, which produced
  // the reviewer-flow bug where Approve/Reject acted on the wrong job. The
  // reducer-level requestId guard in jobs.slice ensures `selectedJob` only
  // ever reflects the most recently dispatched detail fetch.
  const selectedJob = useSelector(selectSelectedJobDetails);
  const job: Job = selectedJob ?? ({} as Job);
  const navigate = useNavigate();

  const { jobId } = useParams<{ jobId: string }>();
  const { groupId } = useParams<{ groupId: string }>();
  const { channelId } = useParams<{ channelId: string }>();
  const dispatch = useDispatch<AppDispatch>();
  const error = useSelector(selectGetJobDetailsError);
  const [showRemoveGMMDialog, setShowRemoveGMMDialog] = useState(false);
  const [showRemoveGMMError, setShowRemoveGMMError] = useState(false);
  const removeGMMError = useSelector(selectRemoveGMMError);
  const jobLoading = useSelector(selectSelectedJobLoading);
  const removeGMMPending = useSelector(selectRemoveGMMLoading);
  const isJobWriter = useSelector(selectIsJobWriter);
  const isSubmissionReviewer = useSelector(selectIsSubmissionReviewer);
  const isSubmissionRejector = useSelector(selectIsSubmissionRejector);
  const jobIdSet = useSelector(selectJobIdSet);
  const isJobOwnerDeleter: boolean = useSelector(selectIsJobOwnerDeleter);
  const canDeleteJob: boolean = isJobWriter || isJobOwnerDeleter;
  const isDestinationGroupNotFound = !job.targetGroupName
  const showLoader: boolean = jobLoading || removeGMMPending;

  const [isJobHistoryPanelOpen, setIsJobHistoryPanelOpen] = useState(false);

  const OpenInNewWindowIcon: IIconProps = { iconName: 'OpenInNewWindow' };

  const resolveReview = () => {
    setCanEditJob(isJobWriter);
    navigate('/');
  };

  const onMessageBarDismiss = (): void => {
    dispatch(setGetJobDetailsError());
  };

  const openInService = (): void => {
    let url;
    if (job?.targetDestinationType === DestinationType.GroupMembership) {
      url = `https://ms.portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Overview/groupId/${job?.targetGroupId}`;
    } else if (job?.targetDestinationType === DestinationType.TeamsChannelMembership) {
      url = `https://teams.microsoft.com/l/channel/${job.targetChannelId}`;
    } else {
      console.error('Unexpected destination:', job?.targetDestinationType);
    }
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const openRunConfiguration = (thresholdExceeded?: { additionsExceeded: boolean; removalsExceeded: boolean }): void => {
    dispatch(setIsEditingExistingJob(true));
    navigate(`/ManageMembership/${jobId ?? job.syncJobId}`, {
      state: {
        currentStep: OnboardingSteps.RunConfiguration,
        jobId: job?.syncJobId,
        thresholdExceededForAdditions: thresholdExceeded?.additionsExceeded ?? false,
        thresholdExceededForRemovals: thresholdExceeded?.removalsExceeded ?? false,
      }
    });
  };

  const openMembershipConfiguration = (): void => {
    dispatch(setIsEditingExistingJob(true));
    navigate(`/ManageMembership/${jobId ?? job.syncJobId}`, { state: { currentStep: OnboardingSteps.MembershipConfiguration, jobId: job?.syncJobId } });
  };

  const onBackToJobsList = (): void => {
    navigate('/');
  };

  const onRemoveGMMButtonClick = (): void => {
    setShowRemoveGMMDialog(true);
  };

  const onDialogClose = () => {
    setShowRemoveGMMDialog(false);
  };

  const onConfirmRemove = async () => {
    try {
      if (jobId === undefined && job.syncJobId === undefined) {
        throw new Error('Job ID is not defined');
      }
      await dispatch(removeGMM({ syncJobId: jobId ?? job.syncJobId }));
      setShowRemoveGMMDialog(false);

      let url;
      if (job?.targetDestinationType === DestinationType.GroupMembership) {
        url = `https://portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Owners/${job?.targetGroupId}/menuId/`;
      } else if (job?.targetDestinationType === DestinationType.TeamsChannelMembership) {
        url = `https://teams.microsoft.com/l/channel/${job.targetChannelId}`;
      } else {
        console.error('Unexpected destination type:', job?.targetDestinationType);
      }
      window.open(url, '_blank', 'noopener,noreferrer');
      navigate('/');
    } catch (error) {
      setShowRemoveGMMError(true);
      throw new Error(`Failed to remove GMM: ${error}`);
    }
  };

  const [canEditJob, setCanEditJob] = useState<boolean>(false);
  const isJobInProgress = job?.status === SyncStatus.InProgress;

  useEffect(() => {
    setCanEditJob(isJobWriter && job?.status !== SyncStatus.PendingReview && job?.status !== SyncStatus.PendingConfiguration && job?.status !== SyncStatus.PendingAutoApproval);
  }, [isJobWriter, job?.status]);

  useEffect(() => {
    dispatch(setPagingBarVisible(false));
    if (jobId) {
      if (jobIdSet === "" || (jobIdSet !== "" && jobIdSet !== jobId)) {
        dispatch(clearSourceParts());
        dispatch(fetchJobDetails({ syncJobId: jobId }));
        dispatch(setJobId(jobId));
        dispatch(setGeneratedTitlesYet(false));
        dispatch(clearTitles());
        dispatch(clearGeneratedHRParts());
        dispatch(clearGeneratedGroupParts());
      }
    }
    if (groupId) {
      dispatch(clearSourceParts());
      dispatch(setGeneratedTitlesYet(false));
      dispatch(clearTitles());
      dispatch(clearGeneratedHRParts());
      dispatch(clearGeneratedGroupParts());
      if (channelId === undefined) {
        dispatch(getGroupDetails(groupId));
      } else {
        dispatch(getChannelDetails({ groupId, channelId }));
      }
    }
  }, [dispatch, jobId, groupId, channelId]);

  // Redirect to NotFound if job fetch fails (job doesn't exist)
  useEffect(() => {
    if (error && !selectedJob && !jobLoading) {
      navigate('/NotFound', { replace: true });
    }
  }, [error, selectedJob, jobLoading, navigate]);

  // Auto-open Run History panel if URL path includes /history
  useEffect(() => {
    if (window.location.pathname.includes('/history')) {
      setIsJobHistoryPanelOpen(true);
    }
  }, []);

  // Sync URL with panel state - add/remove /history suffix
  useEffect(() => {
    if (!jobId) return;

    const currentPath = window.location.pathname;
    const hasHistorySuffix = currentPath.includes('/history');

    if (isJobHistoryPanelOpen && !hasHistorySuffix) {
      // Panel opened - add /history to URL
      navigate(`/JobDetails/${jobId}/history`, { replace: true });
    } else if (!isJobHistoryPanelOpen && hasHistorySuffix) {
      // Panel closed - remove /history from URL
      navigate(`/JobDetails/${jobId}`, { replace: true });
    }
  }, [isJobHistoryPanelOpen, jobId, navigate]);


  return (
    <Page>
      <PageHeader
        onBackToDashboardButtonClick={onBackToJobsList}
      />
      {showLoader ? <Loader />
        : ( <>
          {/* Error Message */}
          <div>
            {error && (
              <MessageBar
                messageBarType={MessageBarType.error}
                isMultiline={false}
                onDismiss={onMessageBarDismiss}
                dismissButtonAriaLabel={
                  strings.JobDetails.MessageBar.dismissButtonAriaLabel as string
                }
              >
                {error}
              </MessageBar>
            )}
            {showRemoveGMMError && (
              <MessageBar
                messageBarType={MessageBarType.error}
                isMultiline={false}
                onDismiss={() => setShowRemoveGMMError(false)}
                dismissButtonAriaLabel={
                  strings.JobDetails.MessageBar.dismissButtonAriaLabel as string
                }
              >
                {strings.JobDetails.Errors.removeGMMError} {removeGMMError}
              </MessageBar>
            )}
          </div>
          {isDestinationGroupNotFound ? (
            <div className={classNames.root}>
              <div className={classNames.notFound}>
                {!job.estimatedPurgeDate
                  ? format(strings.JobDetails.notFoundWithoutPurgeDate, job.targetGroupId)
                  : format(
                      strings.JobDetails.notFound,
                      job.targetGroupId,
                      job.lastSuccessfulRunTime ? new Date(job.lastSuccessfulRunTime).toLocaleDateString() : 'Unknown',
                      job.estimatedPurgeDate ? new Date(job.estimatedPurgeDate).toLocaleDateString() : 'Unknown'
                    )
                }
              </div>
            </div>
          ) : ( <>
          { selectedJob && (
          <div className={classNames.root}>
              <div className={classNames.pageHeaderRow}>
                <MembershipDetails job={job} classNames={classNames} />
                <ActionButton
                  id="job-history-button"
                  iconProps={{ iconName: 'History' }}
                  text={strings.JobDetails.Panel.history}
                  onClick={() => setIsJobHistoryPanelOpen(true)}
                />
              </div>
              {(job.status === SyncStatus.PendingReview || job.status === SyncStatus.PendingConfiguration || job.status === SyncStatus.PendingAutoApproval) && (
                <div className={classNames.stickyBannerWrapper}>
                  <div className={classNames.pendingBanner}>
                    <Icon
                      iconName={job.status === SyncStatus.PendingConfiguration && (isSubmissionReviewer || isSubmissionRejector) ? 'HourGlass' : 'AlarmClock'}
                      className={classNames.clockIcon}
                    />
                    <Text className={classNames.pendingBannerText}>
                      <b>
                        {job.status === SyncStatus.PendingConfiguration && (isSubmissionReviewer || isSubmissionRejector)
                          ? strings.JobDetails.labels.pendingConfiguration
                          : strings.JobDetails.labels.pendingReview}.
                      </b>
                      {' '}
                      {(isSubmissionReviewer || isSubmissionRejector)
                        ? (job.status === SyncStatus.PendingConfiguration
                          ? strings.JobDetails.labels.pendingConfigurationInstructions
                          : strings.JobDetails.labels.pendingReviewInstructions)
                        : strings.JobDetails.labels.pendingReviewDescription}
                    </Text>
                  </div>
                </div>
              )}
              <ContentContainer
                title={strings.JobDetails.labels.membershipStatus}
                children={<MembershipStatusContent job={job} resolveReview={resolveReview} classNames={classNames} />}
                removeButton={true}
              />
              <MembershipBusinessJustification job={job} classNames={classNames} />
              <ContentContainer
                title={strings.JobDetails.labels.destination}
                actionButtons={[
                  { text: job.targetDestinationType === DestinationType.TeamsChannelMembership ? strings.JobDetails.openInTeams : strings.JobDetails.openInAzure, icon: OpenInNewWindowIcon, onClick: openInService }
                ]}
                children={<MembershipDestination job={job} classNames={classNames} />}
              />
              <ContentContainer
                title={strings.JobDetails.labels.configuration}
                children={<RunConfiguration job={job} classNames={classNames} />}
                actionButtons={
                  canEditJob
                  ? [{
                      text: strings.JobDetails.editButton,
                      icon: { iconName: 'Edit' },
                      onClick: openRunConfiguration,
                      disabled: isJobInProgress,
                      disabledReason: isJobInProgress ? strings.JobDetails.editDisabledInProgress : undefined
                    }]
                  : []
                }
              />
              <ContentContainer
                title={strings.JobDetails.labels.sourceParts}
                actionButtons={
                  canEditJob
                  ? [{
                      text: strings.JobDetails.editButton,
                      icon: { iconName: 'Edit' },
                      onClick: openMembershipConfiguration,
                      disabled: isJobInProgress,
                      disabledReason: isJobInProgress ? strings.JobDetails.editDisabledInProgress : undefined
                    }]
                  : []
                }
                children={
                  <>
                    <MembershipConfiguration
                      isEditable={false}
                    />
                    <SubmissionReviewActions job={job} resolveReview={resolveReview} classNames={classNames} />
                  </>
                }
              />
            </div>
          )}
          {jobId && (
            <JobHistoryPanel
              isOpen={isJobHistoryPanelOpen}
              dismissPanel={() => setIsJobHistoryPanelOpen(false)}
              jobId={jobId}
              onEditRules={openMembershipConfiguration}
              onEditThreshold={openRunConfiguration}
            />
          )}
        </>
      )}
      <div className={isDestinationGroupNotFound ? classNames.removeGMMNotFound : classNames.removeGMM}>
        {canDeleteJob &&
        <ActionButton
          data-testid="remove-button"
          iconProps={{ iconName: 'Delete' }}
          title={strings.JobDetails.labels.removeGMM}
          ariaLabel={strings.JobDetails.labels.removeGMM}
          onClick={onRemoveGMMButtonClick}>
          {strings.JobDetails.labels.removeGMM}
        </ActionButton>}
      </div>
      <Dialog
        hidden={!showRemoveGMMDialog}
        onDismiss={onDialogClose}
        dialogContentProps={{
          type: DialogType.normal,
          title: strings.JobDetails.labels.removeGMM,
          subText: strings.JobDetails.labels.removeGMMWarning
        }}
        modalProps={{
          isBlocking: true
        }}
      >
        <DialogFooter>
          <PrimaryButton
            data-testid="remove-confirmation-button"
            onClick={onConfirmRemove}
            text={strings.JobDetails.labels.removeGMMConfirmation}
            styles={{ root: { padding: '16px' } }}
          />
          <DefaultButton onClick={onDialogClose} text={strings.cancel} />
        </DialogFooter>
      </Dialog>
      </>
      )}
    </Page >
  );
};

const MembershipDetails: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const { job, classNames } = props;
  const strings = useStrings();

  const groupName = job.targetDestinationType === DestinationType.TeamsChannelMembership
    ? `${job.targetGroupName}: ${job.targetChannelName}`
    : job.targetGroupName;

  const initials = (groupName ?? '')
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((word) => word[0])
    .join('')
    .toUpperCase();

  return (
    <div className={classNames.headerTitleGroup}>
      <Text className={classNames.title} block>
        {strings.JobDetails.labels.pageTitle}
      </Text>
      {groupName && (
        <div className={classNames.groupPill}>
          <div className={classNames.groupAvatar}>{initials}</div>
          <Text className={classNames.groupName}>{groupName}</Text>
        </div>
      )}
    </div>
  )
}

const MembershipBusinessJustification: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const { job, classNames } = props;
  const strings = useStrings();
  const isSubmissionReviewer = useSelector(selectIsSubmissionReviewer);
  const isSubmissionRejector = useSelector(selectIsSubmissionRejector);
  const jobDetails = useSelector(selectSelectedJobDetails);
  const lastModifiedUserProfile = useSelector(selectLastModifiedUserProfile);
  const jobChanges: SyncJobChange[] | undefined = useSelector(selectSelectedJobChanges);
  const justification = jobChanges?.[0]?.businessJustification;

  const personaProps: IPersonaSharedProps = {
    imageUrl: lastModifiedUserProfile?.photoUrl,
    text: lastModifiedUserProfile?.displayName
  };

  // Match the existing conditions: only render while the job is pending review
  // and an actual business justification is available to display.
  const shouldShow = (isSubmissionReviewer || isSubmissionRejector)
    && job?.status === SyncStatus.PendingReview
    && !!justification;

  if (!shouldShow) {
    return null;
  }

  return (
    <ContentContainer title={strings.JobDetails.labels.businessJustification}>
      <div className={classNames.businessJustificationColumns}>
        <div className={classNames.businessJustificationRequestedBy}>
          <InfoLabel
            label={strings.JobDetails.labels.requestedBy}
            description={strings.JobDetails.descriptions.requestedBy}
          />
          <div className={classNames.itemData}>
            {jobDetails != null ? (
              lastModifiedUserProfile?.photoUrl === "ErrorNonExistentStorage" ? (
                <div className={classNames.itemData}>
                  <Text variant="medium" block>
                    {lastModifiedUserProfile?.displayName}
                  </Text>
                  <Text variant="medium" block>
                    {jobDetails.lastModifiedByObjectId}
                  </Text>
                </div>
              ) : (
                <Persona
                  {...personaProps}
                  text={lastModifiedUserProfile?.displayName}
                  size={PersonaSize.size32}
                  hidePersonaDetails={false}
                  imageAlt={lastModifiedUserProfile?.displayName}
                />
              )
            ) : (
              <Shimmer width="100%" />
            )}
          </div>
        </div>
        <div className={classNames.businessJustificationText}>
          <InfoLabel
            label={strings.JobDetails.labels.justification}
            description={strings.JobDetails.descriptions.justification}
          />
          <div className={classNames.itemData}>
            {justification}
          </div>
        </div>
      </div>
    </ContentContainer>
  );
}

const MembershipStatusContent: React.FunctionComponent<IStatusContentProps> = (
  props: IStatusContentProps
) => {
  const dispatch = useDispatch<AppDispatch>();
  const strings = useStrings();
  const theme = useTheme();
  const { job, classNames } = props;
  const { jobId } = useParams<{ jobId: string }>();
  const isSubmissionReviewer = useSelector(selectIsSubmissionReviewer);
  const isSubmissionRejector = useSelector(selectIsSubmissionRejector);
  const patchError = useSelector(selectPatchJobDetailsError);
  const patchResponse = useSelector(selectPatchJobDetailsResponse);
  // Status & enabled are derived directly from the Redux-backed `job` prop;
  // mirroring them into local useState caused stale UI when the parent
  // refetched after an approve/reject (the local copy lagged a render behind
  // and reflected the previous job's status during rapid renavigation).
  const jobStatus = job?.status ?? '';
  const isJobEnabled = job?.enabledOrNot ?? false;
  const jobDetails = useSelector(selectSelectedJobDetails);
  const isJobEnabler = useSelector(selectIsJobOwnerEnabler);
  const isJobWriter = useSelector(selectIsJobWriter);
  const canEnableJob = isJobEnabler || isJobWriter;
  const lastModifiedOnBehalfOfUserProfile = useSelector(selectLastModifiedOnBehalfOfUserProfile);
  const lastModifiedOnBehalfOfUserProps: IPersonaSharedProps = {
    imageUrl: lastModifiedOnBehalfOfUserProfile?.photoUrl,
    text: lastModifiedOnBehalfOfUserProfile?.displayName
  };
  const jobChanges: SyncJobChange[] | undefined = useSelector(selectSelectedJobChanges);
  const lastChange = jobChanges?.[0];
  const businessJustification = useSelector(manageMembershipBusinessJustification);
  const hasHiddenMembershipSources = job?.hasHiddenMembershipSources ?? false;
  const [showSyncNowDialog, setShowSyncNowDialog] = useState(false);
  const [ignoreThresholdOnce, setIgnoreThresholdOnce] = useState(false);
  const [isSyncingNow, setIsSyncingNow] = useState(false);
  const [syncNowError, setSyncNowError] = useState<string | null>(null);
  const [syncNowUsageCount, setSyncNowUsageCount] = useState(0);
  const [syncNowLimit, setSyncNowLimit] = useState(3);
  const canScheduleNow = isSubmissionReviewer;

  // Re-fetch the job-changes feed whenever the route's jobId (or the loaded
  // selectedJob's syncJobId) changes. We deliberately depend on the *id*, not
  // on the whole `job` object — `job` is recreated on every Redux update,
  // which previously caused this effect to re-run on every render.
  useEffect(() => {
    const targetSyncJobId = jobId ?? job?.syncJobId;
    if (!targetSyncJobId) return;
    dispatch(fetchJobChanges({ syncJobId: targetSyncJobId }));
  }, [dispatch, jobId, job?.syncJobId]);

  useEffect(() => {
    if (jobDetails && jobDetails.lastModifiedByObjectId) {
      dispatch(getProfilePhotoUsingId({ id: jobDetails.lastModifiedByObjectId, type: 'lastModifiedBy' }));
    }
    if (jobDetails && jobDetails.lastModifiedOnBehalfOfObjectId) {
      dispatch(getProfilePhotoUsingId({ id: jobDetails.lastModifiedOnBehalfOfObjectId, type: 'lastModifiedOnBehalfOf' }));
    }
  }, [dispatch, jobDetails]);

  // Fetch sync now usage from server
  useEffect(() => {
    if (canScheduleNow) {
      dispatch(fetchSyncNowUsage()).then((result) => {
        if (fetchSyncNowUsage.fulfilled.match(result)) {
          setSyncNowUsageCount(result.payload.count);
          setSyncNowLimit(result.payload.limit);
        }
      });
    }
  }, [dispatch, canScheduleNow]);

  const refreshSyncNowUsage = async () => {
    const result = await dispatch(fetchSyncNowUsage());
    if (fetchSyncNowUsage.fulfilled.match(result)) {
      setSyncNowUsageCount(result.payload.count);
      setSyncNowLimit(result.payload.limit);
    }
  };

  const handleSyncNowClick = () => {
    if (syncNowUsageCount >= 0 && syncNowLimit >= 0 && syncNowUsageCount >= syncNowLimit) {
      setSyncNowError(strings.JobDetails.Errors.syncNowLimitExceeded);
      return;
    }
    setShowSyncNowDialog(true);
    setIgnoreThresholdOnce(false);
  };

  const handleSyncNowDialogClose = () => {
    setShowSyncNowDialog(false);
    setIgnoreThresholdOnce(false);
  };

  const handleConfirmSyncNow = async () => {
    if (syncNowUsageCount >= 0 && syncNowLimit >= 0 && syncNowUsageCount >= syncNowLimit) {
      setSyncNowError(strings.JobDetails.Errors.syncNowLimitExceeded);
      setShowSyncNowDialog(false);
      return;
    }

    setIsSyncingNow(true);
    setSyncNowError(null);

    try {
      // Calculate scheduledDate to be 5 minutes from now
      const scheduledDate = new Date();
      scheduledDate.setMinutes(scheduledDate.getMinutes() + 5);
      
      const patchOperation: Array<{ op: string; path: string; value: string | boolean }> = [
        {
          op: "replace",
          path: "/ScheduledDate",
          value: scheduledDate.toISOString()
        }
      ];

      if (ignoreThresholdOnce) {
        patchOperation.push({
          op: "replace",
          path: "/IgnoreThresholdOnce",
          value: true
        });
      }

      const patchRequest: PatchJobRequest = {
        syncJobId: jobId ?? job.syncJobId,
        patchOperation,
        changeReason: SyncJobChangeReason.ScheduledNow,
        businessJustification: strings.JobDetails.Panel.syncNowJustification
      };

      const response = await dispatch(patchJobDetails(patchRequest)).unwrap();
      if (response.ok) {
        await refreshSyncNowUsage();
        setShowSyncNowDialog(false);
        // Refresh job details to show updated schedule
        dispatch(fetchJobDetails({ syncJobId: jobId ?? job.syncJobId }));
      } else if (response.statusCode === 429 || response.errorCode === 'ScheduleNowLimitExceeded') {
        setSyncNowError(strings.JobDetails.Errors.syncNowLimitExceeded);
        await refreshSyncNowUsage();
      } else {
        setSyncNowError(strings.JobDetails.Errors.syncNowError);
      }
    } catch (error) {
      setSyncNowError(strings.JobDetails.Errors.syncNowError);
    } finally {
      setIsSyncingNow(false);
    }
  };

  const updateJobStatus = async (
    newStatus: string,
    changeReason: SyncJobChangeReason,
    rejectionBusinessJustification?: string,
    options?: { refetch?: boolean }
  ) => {
    if (jobId === undefined && job.syncJobId === undefined) {
      throw new Error('Job ID is not defined');
    }

    const patchOperation = [{
      op: "replace",
      path: "/Status",
      value: newStatus
    }];

    const patchRequest: PatchJobRequest = {
      syncJobId: jobId ?? job.syncJobId,
      patchOperation,
      changeReason,
      businessJustification: rejectionBusinessJustification ?? businessJustification ?? ''
    };

    try {
      const response = await dispatch(patchJobDetails(patchRequest)).unwrap();
      if (response.ok) {
        // Optimistic status so consumers see the change before refetch.
        dispatch(setSelectedJobStatus(newStatus));
        dispatch(setSelectedJobEnabled(newStatus === SyncStatus.Idle));
      } else if (response.responseData && response.responseData[0] === "SubmitterNotOwner") {
        dispatch(setSelectedJobStatus(SyncStatus.SubmissionRejected));
        dispatch(setSelectedJobEnabled(false));
      }

      // Skip refetch when the caller is about to navigate away (Approve/Reject).
      if (options?.refetch !== false) {
        await dispatch(fetchJobDetails({ syncJobId: jobId ?? job.syncJobId }));
      }
    } catch (error) {
      throw new Error('Failed to update job status');
    }
  };

  const handleStatusChange = (ev: React.MouseEvent<HTMLElement>, checked?: boolean) => {
    const newStatus = isJobEnabled ? SyncStatus.CustomerPaused : SyncStatus.Idle;
    updateJobStatus(newStatus, SyncJobChangeReason.StatusUpdate);
  };

  const displayMessage = (patchResponse?: PatchJobResponse): string  => {
    if (!patchResponse) {
      return "";
    }
    const errorCode = patchResponse.errorCode;

    switch (errorCode) {
      case 'JobInProgress':
        return strings.JobDetails.Errors.jobInProgress;
      case 'Forbidden':
        return strings.JobDetails.Errors.forbidden;
      case 'InternalError':
        return strings.JobDetails.Errors.internalError;
      case 'SubmitterNotOwner':
        return strings.JobDetails.Errors.submitterNotOwner;
      case 'ReviewerCannotReviewOwnSubmission':
        return strings.JobDetails.Errors.reviewerCannotReviewOwnSubmission;
      default:
        return "";
    }
  };

  return (
    <div>
      {hasHiddenMembershipSources && jobStatus === SyncStatus.PendingReview && (
        <div className={classNames.hiddenMembershipWarningContainer}>
          <Icon iconName="Hide" className={classNames.hiddenMembershipWarningIcon} />
          <Text className={classNames.hiddenMembershipWarningText}>
            {strings.JobDetails.labels.hiddenMembershipWarning}
          </Text>
        </div>
      )}
      <div className={classNames.membershipStatusContainer}>
      <div className={classNames.membershipStatusHeader}>
      <div className={classNames.membershipStatusControls}>
        <div className={classNames.toggleRow}>
          <label className={classNames.toggleLabel}>{strings.JobDetails.labels.sync}</label>
          <Toggle
            title={isJobEnabled ? strings.JobDetails.labels.enabled : strings.JobDetails.labels.disabled}
            inlineLabel={true}
            checked={isJobEnabled}
            onChange={handleStatusChange}
            disabled={!canEnableJob || jobStatus === SyncStatus.PendingReview || jobStatus === SyncStatus.PendingConfiguration || jobStatus === SyncStatus.PendingAutoApproval || jobStatus === SyncStatus.SubmissionRejected}
          />
        </div>
        <div className={isJobEnabled ? classNames.jobEnabled : classNames.jobDisabled}>
          {isJobEnabled ? strings.JobDetails.labels.enabled : strings.JobDetails.labels.disabled}
        </div>
      </div>
      {canScheduleNow && isJobEnabled && jobStatus === SyncStatus.Idle && (
        <ActionButton
          className={classNames.syncNowButton}
          iconProps={{ iconName: 'Sync', styles: { root: { color: theme.palette.themePrimary } } }}
          title={strings.JobDetails.labels.syncNowButtonAriaLabel}
          ariaLabel={strings.JobDetails.labels.syncNowButtonAriaLabel}
          text={strings.JobDetails.labels.syncNow}
          onClick={handleSyncNowClick}
          disabled={isSyncingNow}
        />
      )}
      </div>
      <div className={classNames.membershipStatusMessage}>
        <div>
          {!patchResponse?.ok && (displayMessage(patchResponse))}
          {syncNowError && (
            <MessageBar
              messageBarType={MessageBarType.error}
              isMultiline={false}
              onDismiss={() => setSyncNowError(null)}
              dismissButtonAriaLabel={strings.close}
            >
              {syncNowError}
            </MessageBar>
          )}
        </div>
        {(jobStatus === SyncStatus.SubmissionRejected) && (
          <div>
            <>
              <Text>{strings.JobDetails.labels.submissionRejected}</Text>
              <Label>{strings.JobDetails.labels.businessJustification}</Label>
                  {lastChange?.businessJustification}
            </>
          </div>
        )}
      </div>

      <div className={classNames.requestor}>
        {(isSubmissionReviewer || isSubmissionRejector) && (jobStatus === SyncStatus.PendingReview) && jobDetails && jobDetails.lastModifiedOnBehalfOfObjectId &&
        (jobDetails.lastModifiedOnBehalfOfObjectId !== jobDetails.lastModifiedByObjectId) && (
        <div>
        <Stack.Item align="start">
          <InfoLabel
            label={strings.JobDetails.labels.requestedOnBehalfOf}
            description={strings.JobDetails.descriptions.requestedOnBehalfOf}
          />
         <div className={classNames.itemData}>
          {jobDetails != null ? (
            (lastModifiedOnBehalfOfUserProfile?.photoUrl === "ErrorNonExistentStorage") ? (
              <div className={classNames.itemData}>
              <Text variant="medium" block>
              {lastModifiedOnBehalfOfUserProfile?.displayName}
              </Text>
              <Text variant="medium" block>
              {jobDetails.lastModifiedOnBehalfOfObjectId}
              </Text>
            </div>
            ) : (
              <Persona
                {...lastModifiedOnBehalfOfUserProps}
                text={lastModifiedOnBehalfOfUserProfile?.displayName}
                size={PersonaSize.size32}
                hidePersonaDetails={false}
                imageAlt={lastModifiedOnBehalfOfUserProfile?.displayName}
              />
            )
          ) : (
            <Shimmer width="100%" />
          )}
        </div>
        </Stack.Item>
        </div>
        )}
        </div>
      </div>
      {/* Sync Now Dialog */}
      <Dialog
        hidden={!showSyncNowDialog}
        onDismiss={isSyncingNow ? undefined : handleSyncNowDialogClose}
        dialogContentProps={{
          type: DialogType.normal,
          title: strings.JobDetails.labels.syncNowDialogTitle,
        }}
        modalProps={{
          isBlocking: true
        }}
        minWidth={480}
        maxWidth={600}
      >
        <Text block style={{ marginBottom: 4 }}>
          {format(strings.JobDetails.labels.syncNowDialogMessage, job.targetGroupName)}
        </Text>
        <Text block style={{ marginBottom: 12 }}>
          {strings.JobDetails.labels.syncNowDialogSubMessage}
        </Text>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 16, color: theme.palette.neutralSecondary, fontSize: 12 }}>
          <Icon iconName="Info" styles={{ root: { fontSize: 14 } }} />
          <Text variant="small">
            {syncNowUsageCount >= 0
              ? format(strings.JobDetails.labels.syncNowUsageLimit, syncNowUsageCount.toString(), syncNowLimit.toString())
              : strings.JobDetails.labels.syncNowUsageUnavailable}
          </Text>
        </div>
        <Checkbox
          label={strings.JobDetails.labels.syncNowIgnoreThresholdCheckbox}
          checked={ignoreThresholdOnce}
          onChange={(_, checked) => setIgnoreThresholdOnce(checked || false)}
          disabled={isSyncingNow}
          styles={{ checkbox: { borderRadius: 4 } }}
        />
        <DialogFooter>
          <DefaultButton onClick={handleSyncNowDialogClose} text={strings.JobDetails.labels.syncNowCancel} disabled={isSyncingNow} styles={{ root: { borderRadius: 4 } }} />
          <PrimaryButton
            onClick={handleConfirmSyncNow}
            disabled={isSyncingNow}
            styles={{ root: { borderRadius: 4 } }}
          >
            {isSyncingNow && (
              <Spinner size={SpinnerSize.xSmall} style={{ marginRight: 8 }} />
            )}
            {strings.JobDetails.labels.syncNowConfirm}
          </PrimaryButton>
        </DialogFooter>
      </Dialog>
    </div>
  )
}

const SubmissionReviewActions: React.FunctionComponent<IStatusContentProps> = (
  props: IStatusContentProps
) => {
  const dispatch = useDispatch<AppDispatch>();
  const strings = useStrings();
  const { job, resolveReview, classNames } = props;
  const { jobId } = useParams<{ jobId: string }>();
  const isSubmissionReviewer = useSelector(selectIsSubmissionReviewer);
  const isSubmissionRejector = useSelector(selectIsSubmissionRejector);
  const jobStatus = job?.status ?? '';
  const businessJustification = useSelector(manageMembershipBusinessJustification);
  const [showRejectionDialog, setShowRejectionDialog] = useState(false);
  const [rejectionFeedback, setRejectionFeedback] = useState('');
  const [isSubmittingRejection, setIsSubmittingRejection] = useState(false);
  // Double-click guard for Approve.
  const [isSubmittingApproval, setIsSubmittingApproval] = useState(false);
  const [rejectionError, setRejectionError] = useState<string | null>(null);

  const updateJobStatus = async (
    newStatus: string,
    changeReason: SyncJobChangeReason,
    rejectionBusinessJustification?: string,
    options?: { refetch?: boolean }
  ) => {
    if (jobId === undefined && job.syncJobId === undefined) {
      throw new Error('Job ID is not defined');
    }

    const patchOperation = [{
      op: "replace",
      path: "/Status",
      value: newStatus
    }];

    const patchRequest: PatchJobRequest = {
      syncJobId: jobId ?? job.syncJobId,
      patchOperation,
      changeReason,
      businessJustification: rejectionBusinessJustification ?? businessJustification ?? ''
    };

    try {
      const response = await dispatch(patchJobDetails(patchRequest)).unwrap();
      if (response.ok) {
        // Optimistic status so consumers see the change before refetch.
        dispatch(setSelectedJobStatus(newStatus));
        dispatch(setSelectedJobEnabled(newStatus === SyncStatus.Idle));
      } else if (response.responseData && response.responseData[0] === "SubmitterNotOwner") {
        dispatch(setSelectedJobStatus(SyncStatus.SubmissionRejected));
        dispatch(setSelectedJobEnabled(false));
      }

      // Skip refetch when the caller is about to navigate away (Approve/Reject).
      if (options?.refetch !== false) {
        await dispatch(fetchJobDetails({ syncJobId: jobId ?? job.syncJobId }));
      }
    } catch (error) {
      throw new Error('Failed to update job status');
    }
  };

  const handleApproveSubmission = async (approved: boolean) => {
    if (!approved) {
      setShowRejectionDialog(true);
      return;
    }
    if (isSubmittingApproval) return;
    setIsSubmittingApproval(true);
    try {
      // Await PATCH so post-nav fetchJobs sees committed state.
      await updateJobStatus(SyncStatus.Idle, SyncJobChangeReason.SubmissionApproved, undefined, { refetch: false });
      const targetJobId = jobId ?? job.syncJobId;
      if (targetJobId) {
        dispatch(removeJobFromList(targetJobId));
      }
      resolveReview();
    } catch {
      // Error surfaced via patchResponse.
    } finally {
      setIsSubmittingApproval(false);
    }
  };

  const handleRejectDialogClose = () => {
    setShowRejectionDialog(false);
    setRejectionFeedback('');
    setIsSubmittingRejection(false);
    setRejectionError(null);
  };

  const handleRejectSubmission = async () => {
    setIsSubmittingRejection(true);
    setRejectionError(null);
    try {
      await updateJobStatus(SyncStatus.SubmissionRejected, SyncJobChangeReason.SubmissionRejected, rejectionFeedback, { refetch: false });
      const targetJobId = jobId ?? job.syncJobId;
      if (targetJobId) {
        dispatch(removeJobFromList(targetJobId));
      }
      resolveReview();
      setShowRejectionDialog(false);
      setRejectionFeedback('');
      setIsSubmittingRejection(false);
    } catch (error) {
      setIsSubmittingRejection(false);
      setRejectionError(strings.JobDetails.Errors.rejectionError);
    }
  };

  if (!(isSubmissionReviewer || isSubmissionRejector) || jobStatus !== SyncStatus.PendingReview) {
    return null;
  }

  return (
    <>
      <div className={classNames.submissionReviewActions}>
        {isSubmissionReviewer && (
          <DefaultButton onClick={() => handleApproveSubmission(true)} disabled={isSubmittingApproval}>
            {isSubmittingApproval && (
              <Spinner size={SpinnerSize.xSmall} style={{ marginRight: 8 }} />
            )}
            {strings.JobDetails.labels.approve}
          </DefaultButton>
        )}
        <PrimaryButton onClick={() => handleApproveSubmission(false)} text={strings.JobDetails.labels.reject} disabled={isSubmittingApproval} />
      </div>
      {/* Rejection Dialog */}
      <Dialog
        hidden={!showRejectionDialog}
        onDismiss={isSubmittingRejection ? undefined : handleRejectDialogClose}
        dialogContentProps={{
          type: DialogType.normal,
          title: strings.JobDetails.labels.rejectionDialogTitle,
          subText: strings.JobDetails.labels.rejectionDialogSubText
        }}
        modalProps={{
          isBlocking: true
        }}
        minWidth={600}
        maxWidth={800}
      >
        {rejectionError && (
          <MessageBar
            messageBarType={MessageBarType.error}
            isMultiline={false}
            onDismiss={() => setRejectionError(null)}
            dismissButtonAriaLabel={strings.close}
          >
            {rejectionError}
          </MessageBar>
        )}
        <TextField
          label={strings.JobDetails.labels.rejectionReasonLabel}
          multiline
          rows={4}
          value={rejectionFeedback}
          onChange={(_, newValue) => setRejectionFeedback(newValue || '')}
          placeholder={strings.JobDetails.labels.rejectionReasonPlaceholder}
          required
          disabled={isSubmittingRejection}
        />
        <DialogFooter>
          <PrimaryButton
            onClick={handleRejectSubmission}
            disabled={!rejectionFeedback.trim() || isSubmittingRejection}
          >
            {isSubmittingRejection && (
              <Spinner size={SpinnerSize.xSmall} style={{ marginRight: 8 }} />
            )}
            {isSubmittingRejection ? strings.JobDetails.labels.submittingRejection : strings.JobDetails.labels.submitRejection}
          </PrimaryButton>
        </DialogFooter>
      </Dialog>
    </>
  );
}

const MembershipDestination: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const strings = useStrings();

  const { job, classNames } = props;

  const itemAlignmentsStackTokens: IStackTokens = {
    childrenGap: 100,
  };

  const mainStackTokens: IStackTokens = {
    childrenGap: 20,
  };

  return (
    <Stack
      enableScopedSelectors
      tokens={mainStackTokens}
    >
      <Stack.Item align="start">
        <Stack
          enableScopedSelectors
          horizontal
          tokens={itemAlignmentsStackTokens}
        >
          <Stack.Item align="start">
            <InfoLabel
              label={strings.JobDetails.labels.type}
              description={strings.JobDetails.descriptions.type}
            />
            <Text className={classNames.itemData} block>
              {destinationTypeLocalization[job?.targetDestinationType] || job?.targetDestinationType}
            </Text>
          </Stack.Item>

          <Stack.Item align="start">
            <Text className={classNames.itemTitle} block>
              {strings.JobDetails.labels.name}
            </Text>
            <Text className={classNames.itemData} block>
              {job?.targetGroupName ?? '-'}
            </Text>
          </Stack.Item>

          <Stack.Item align="start">
            <InfoLabel
              label={strings.JobDetails.labels.ID}
              description={strings.JobDetails.descriptions.id}
            />
            <Text className={classNames.itemData} block>
              {job?.targetGroupId}
            </Text>
          </Stack.Item>
          {job?.targetDestinationType === DestinationType.TeamsChannelMembership &&
            <Stack.Item align="start">
              <Text className={classNames.itemTitle} block>
                {strings.JobDetails.labels.channelName}
              </Text>
              <Text className={classNames.itemData} block>
                {job.targetChannelName ?? '-'}
              </Text>
            </Stack.Item>
          }
          {job?.targetDestinationType === DestinationType.TeamsChannelMembership &&
            <Stack.Item align="start">
              <Text className={classNames.itemTitle} block>
                {strings.JobDetails.labels.channelId}
              </Text>
              <Text className={classNames.itemData} block>
                {job.targetChannelId ?? '-'}
              </Text>
            </Stack.Item>
          }
        </Stack>
      </Stack.Item>
        <EndpointsList
          endpoints={job.endpoints}
          groupName={job.targetGroupName}
          vivaEngageUrl={job.vivaEngageUrl}
          linksTitle={strings.JobDetails.labels.groupLinks}
          horizontal={true}
        />
    </Stack>
  )
}

const RunConfiguration: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const strings = useStrings();
  const { job, classNames } = props;
  const jobDetails = useSelector(selectSelectedJobDetails);

  const itemAlignmentsStackTokens: IStackTokens = {
    childrenGap: 15
  };

  const hoursMessage: React.CSSProperties = {
    fontWeight: 100
  }
  const SQL_MIN_DATE = new Date('1753-01-01T00:00:00');

  function splitDateString(value: string) {
    const isEmpty = value === '';
    if (isEmpty) {
      return ['-', ''];
    }

    const spaceIndex = value.indexOf(' ');
    const datePart = value.substring(0, spaceIndex);
    const hoursPart = value.substring(spaceIndex + 1);

    const parsedDate = new Date(datePart);

    const isMinDate = parsedDate <= SQL_MIN_DATE;

    return [isMinDate ? '' : datePart, isMinDate ? '-' : hoursPart];
  }

  const lastRunDetails = splitDateString(job?.lastSuccessfulRunTime ?? '');
  const nextRunDetails = splitDateString(job?.estimatedNextRunTime ?? '');

  return (
    <Stack
      enableScopedSelectors
      tokens={itemAlignmentsStackTokens}
      horizontal
    >
      <Stack.Item align="start">
        <InfoLabel
          label={strings.JobDetails.labels.startDate}
          description={strings.JobDetails.descriptions.startDate}
        />
        <div className={classNames.itemData}>
          {jobDetails != null ? (
            <Text variant="medium" block>
              {new Date(jobDetails.startDate) <= SQL_MIN_DATE
                ? strings.ManageMembership.labels.ASAP
                : new Intl.DateTimeFormat().format(new Date(jobDetails.startDate))}
            </Text>
          ) : (
            <Shimmer width="100%" />
          )}
        </div>
      </Stack.Item>

      {/* <Stack.Item align="start"> // Hidden until feature is enabled
        <InfoLabel
          label={strings.JobDetails.labels.endDate}
          description={strings.JobDetails.descriptions.endDate}
        />
        <div className={classNames.itemData}>
          <Text variant="medium" block>
            00/00/0000
          </Text>
        </div>
      </Stack.Item> */}

      <Stack.Item align="start">
        <InfoLabel
          label={strings.JobDetails.labels.lastRun}
          description={strings.JobDetails.descriptions.lastRun}
        />
        <div className={classNames.itemData}>
          <Text variant="medium" block>
            {lastRunDetails[0]}
          </Text>
          <Text style={hoursMessage} variant="medium" block>
            {lastRunDetails[1]}
          </Text>
        </div>
      </Stack.Item>

      <Stack.Item align="start">
        <InfoLabel
          label={strings.JobDetails.labels.nextRun}
          description={strings.JobDetails.descriptions.nextRun}
        />
        <div className={classNames.itemData}>
          <Text variant="medium" block>
            {nextRunDetails[0]}
          </Text>
          <Text style={hoursMessage} variant="medium" block>
            {nextRunDetails[1]}
          </Text>
        </div>
      </Stack.Item>

      <Stack.Item align="start">
        <InfoLabel
          label={strings.JobDetails.labels.frequency}
          description={strings.JobDetails.descriptions.frequency}
        />
        <div className={classNames.itemData}>
          {jobDetails != null ? (
            <Text variant="medium" block>
              {format(strings.JobDetails.labels.frequencyDescription, job.period)}
            </Text>
          ) : (
            <Shimmer width="100%" />
          )}
        </div>
      </Stack.Item>

      <Stack.Item align="start">
        <InfoLabel
          label={strings.JobDetails.labels.increaseThreshold}
          description={strings.JobDetails.descriptions.increaseThreshold}
        />
        <div className={classNames.itemData}>
          {jobDetails != null ? (
            <Text variant="medium" block>
              {jobDetails.thresholdPercentageForAdditions === -1 ? `${strings.JobDetails.labels.noThresholdSet}` : `${jobDetails.thresholdPercentageForAdditions}%`}
            </Text>
          ) : (
            <Shimmer width="100%" />
          )}
        </div>
      </Stack.Item>

      <Stack.Item align="start">
        <InfoLabel
          label={strings.JobDetails.labels.decreaseThreshold}
          description={strings.JobDetails.descriptions.decreaseThreshold}
        />
        <div className={classNames.itemData}>
          {jobDetails != null ? (
            <Text variant="medium" block>
              {jobDetails.thresholdPercentageForRemovals === -1 ? `${strings.JobDetails.labels.noThresholdSet}` : `${jobDetails.thresholdPercentageForRemovals}%`}
            </Text>
          ) : (
            <Shimmer width="100%" />
          )}
        </div>
      </Stack.Item>
    </Stack>
  )
};
