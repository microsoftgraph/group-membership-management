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
  SpinnerSize
} from '@fluentui/react';

import {
  Stack,
  type IStackTokens
} from '@fluentui/react/lib/Stack';
import React, { useEffect, useState } from 'react';
import { useSelector, useDispatch } from 'react-redux';
import { useNavigate, useLocation, useParams } from 'react-router-dom';
import { InfoLabel } from '../../components/InfoLabel';
import { PageHeader } from '../../components/PageHeader';
import { type Job } from '../../models/Job';
import { type AppDispatch } from '../../store';
import { fetchJobChanges, fetchJobDetails, getChannelDetails, getGroupDetails, patchJobDetails, removeGMM } from '../../store/jobDetails.api';
import {
  selectSelectedJobDetails,
  setGetJobDetailsError,
  selectGetJobDetailsError,
  selectPatchJobDetailsResponse,
  selectPatchJobDetailsError,
  selectRemoveGMMError,
  selectRemoveGMMLoading,
  selectSelectedJobLoading,
  selectSelectedJobChanges
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
import { selectIsJobOwnerDeleter, selectIsJobOwnerEnabler, selectIsJobWriter, selectIsSubmissionReviewer } from '../../store/roles.slice';
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
  const location = useLocation();
  const job: Job = useSelector(selectSelectedJobDetails) ?? location.state?.item ?? {};
  const navigate = useNavigate();

  const { jobId } = useParams<{ jobId: string }>();
  const { groupId } = useParams<{ groupId: string }>();
  const { channelId } = useParams<{ channelId: string }>();
  const dispatch = useDispatch<AppDispatch>();
  const error = useSelector(selectGetJobDetailsError);
  const selectedJob = useSelector(selectSelectedJobDetails);
  const [showRemoveGMMDialog, setShowRemoveGMMDialog] = useState(false);
  const [showRemoveGMMError, setShowRemoveGMMError] = useState(false);
  const removeGMMError = useSelector(selectRemoveGMMError);
  const jobLoading = useSelector(selectSelectedJobLoading);
  const removeGMMPending = useSelector(selectRemoveGMMLoading);
  const isJobWriter = useSelector(selectIsJobWriter);
  const isJobOwnerDeleter: boolean = useSelector(selectIsJobOwnerDeleter);
  const canDeleteJob: boolean = isJobWriter || isJobOwnerDeleter;
  const showLoader: boolean = jobLoading || removeGMMPending;

  const [isJobHistoryPanelOpen, setIsJobHistoryPanelOpen] = useState(false);
  
  const OpenInNewWindowIcon: IIconProps = { iconName: 'OpenInNewWindow' };

  const resolveReview = () => {
    setCanEditJob(isJobWriter);
  };

  const onMessageBarDismiss = (): void => {
    dispatch(setGetJobDetailsError());
  };

  const openInService = (): void => {
    var url;
    if (job?.targetDestinationType === DestinationType.GroupMembership) {
      url = `https://ms.portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Overview/groupId/${job?.targetGroupId}`;
    } else if (job?.targetDestinationType === DestinationType.TeamsChannelMembership) {
      url = `https://teams.microsoft.com/l/channel/${job.targetChannelId}`;
    } else {
      console.error('Unexpected destination:', job?.targetDestinationType);
    }
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const openRunConfiguration = (): void => {
    dispatch(setIsEditingExistingJob(true));
    navigate(`/ManageMembership/${jobId ?? job.syncJobId}`, { state: { currentStep: OnboardingSteps.RunConfiguration, jobId: job?.syncJobId } });
  };

  const openMembershipConfiguration = (): void => {
    dispatch(setIsEditingExistingJob(true));
    navigate(`/ManageMembership/${jobId ?? job.syncJobId}`, { state: { currentStep: OnboardingSteps.MembershipConfiguration, jobId: job?.syncJobId } });
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
      
      var url;
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

  useEffect(() => {
    setCanEditJob(isJobWriter && job?.status !== SyncStatus.PendingReview);
  }, [isJobWriter, job?.status]);
  
  useEffect(() => {
    dispatch(setPagingBarVisible(false));
    if (jobId) {
      dispatch(clearSourceParts());
      dispatch(fetchJobDetails({ syncJobId: jobId }));
    }
    if (groupId && channelId === undefined) {
      dispatch(getGroupDetails(groupId));
    }
    if (groupId && channelId) {
      dispatch(getChannelDetails({ groupId, channelId }));
    }
  }, [dispatch, jobId, groupId, channelId]);
   

  return (
    <Page>
      <PageHeader />
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
          {job.status === SyncStatus.DestinationGroupNotFound ? (
            <div className={classNames.root}>
              <div className={classNames.notFound}>
                {format(strings.JobDetails.notFound, job.targetGroupId)}
              </div>
            </div>
          ) : ( <>
          { selectedJob && (
          <div className={classNames.root}>
              <div className={classNames.historyButtonContainer}>
                <ActionButton
                  iconProps={{ iconName: 'History' }}
                  text={strings.JobDetails.Panel.history}
                  onClick={() => setIsJobHistoryPanelOpen(true)}
                />
              </div>
              <MembershipDetails job={job} classNames={classNames} />
              <ContentContainer
                title={strings.JobDetails.labels.membershipStatus}
                children={<MembershipStatusContent job={job} resolveReview={resolveReview} classNames={classNames} />}
                removeButton={true}
              />
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
                  ? [{ text: strings.JobDetails.editButton, icon: { iconName: 'Edit' }, onClick: openRunConfiguration }]
                  : []
                }
              />
              <ContentContainer
                title={strings.JobDetails.labels.sourceParts}
                actionButtons={
                  canEditJob
                  ? [{ text: strings.JobDetails.editButton, icon: { iconName: 'Edit' }, onClick: openMembershipConfiguration }]
                  : []
                }
                children={
                  <MembershipConfiguration 
                    isEditable={false}
                  />}
              />
            </div>
          )}
          {jobId && (
            <JobHistoryPanel
              isOpen={isJobHistoryPanelOpen}
              dismissPanel={() => setIsJobHistoryPanelOpen(false)}
              jobId={jobId}
            />
          )}
        </>
      )}      
      <div className={classNames.removeGMM}>
        {canDeleteJob &&
        <ActionButton
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
  const { classNames } = props;
  const strings = useStrings();

  return (
    <div className={classNames.card}>
      <div>
        <Text className={classNames.title} block>
          {props.job.targetDestinationType === DestinationType.GroupMembership && `${strings.JobDetails.labels.pageTitle} - ${props.job.targetGroupName}`}
          {props.job.targetDestinationType === DestinationType.TeamsChannelMembership && `${strings.JobDetails.labels.pageTitle} - ${props.job.targetGroupName}: ${props.job.targetChannelName}`}
        </Text>
      </div>
      {/* <div> // Hidden until feature is enabled
        <Text className={classNames.subtitle}>
          {strings.JobDetails.labels.lastModifiedby}
          <Text variant="medium" style={{ marginLeft: 5 }}>
            {'DATA UNAVAILABLE'}
          </Text>
        </Text>
      </div> */}
    </div>
  )
}

const MembershipStatusContent: React.FunctionComponent<IStatusContentProps> = (
  props: IStatusContentProps
) => {
  const dispatch = useDispatch<AppDispatch>();
  const strings = useStrings();
  const { job, resolveReview, classNames } = props;
  const { jobId } = useParams<{ jobId: string }>();
  const isSubmissionReviewer = useSelector(selectIsSubmissionReviewer);
  const patchError = useSelector(selectPatchJobDetailsError);
  const patchResponse = useSelector(selectPatchJobDetailsResponse);
  const [jobStatus, setJobStatus] = useState(job.status);
  const jobDetails = useSelector(selectSelectedJobDetails);
  const [isJobEnabled, setIsJobEnabled] = useState(job.enabledOrNot);
  const isJobEnabler = useSelector(selectIsJobOwnerEnabler);
  const isJobWriter = useSelector(selectIsJobWriter);
  const canEnableJob = isJobEnabler || isJobWriter;
  const lastModifiedUserProfile = useSelector(selectLastModifiedUserProfile);
  const lastModifiedOnBehalfOfUserProfile = useSelector(selectLastModifiedOnBehalfOfUserProfile);
  const personaProps: IPersonaSharedProps = {
    imageUrl: lastModifiedUserProfile?.photoUrl,
    text: lastModifiedUserProfile?.displayName
  };
  const lastModifiedOnBehalfOfUserProps: IPersonaSharedProps = {
    imageUrl: lastModifiedOnBehalfOfUserProfile?.photoUrl,
    text: lastModifiedOnBehalfOfUserProfile?.displayName
  };
  const jobChanges: SyncJobChange[] | undefined = useSelector(selectSelectedJobChanges);
  const lastChange = jobChanges?.[0];
  const businessJustification = useSelector(manageMembershipBusinessJustification);
  const [loadingJobChanges, setLoadingJobChanges] = useState(true);
  const [showRejectionDialog, setShowRejectionDialog] = useState(false);
  const [rejectionFeedback, setRejectionFeedback] = useState('');
  const [isSubmittingRejection, setIsSubmittingRejection] = useState(false);
  const [rejectionError, setRejectionError] = useState<string | null>(null);

  useEffect(() => {
    setJobStatus(job?.status ?? '');
    setIsJobEnabled(job?.enabledOrNot ?? false);
    const fetchChanges = async () => {
      await dispatch(fetchJobChanges({ syncJobId: jobId ?? job.syncJobId }));
      setLoadingJobChanges(false);
    };
    fetchChanges();
  }, [job, dispatch, jobId]);

  useEffect(() => {
    if (jobDetails && jobDetails.lastModifiedByObjectId) {
      dispatch(getProfilePhotoUsingId({ id: jobDetails.lastModifiedByObjectId, type: 'lastModifiedBy' }));
    }
    if (jobDetails && jobDetails.lastModifiedOnBehalfOfObjectId) {
      dispatch(getProfilePhotoUsingId({ id: jobDetails.lastModifiedOnBehalfOfObjectId, type: 'lastModifiedOnBehalfOf' }));
    }
  }, [dispatch, jobDetails]);

  const updateJobStatus = async (newStatus: string, changeReason: SyncJobChangeReason, rejectionBusinessJustification?: string) => {
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
        setJobStatus(newStatus);
        setIsJobEnabled(newStatus === SyncStatus.Idle);
      } else if (response.responseData && response.responseData[0] === "SubmitterNotOwner") {
        setJobStatus(SyncStatus.SubmissionRejected);
        setIsJobEnabled(false);
      }

      await dispatch(fetchJobDetails({ syncJobId: jobId ?? job.syncJobId }));
    } catch (error) {
      throw new Error('Failed to update job status');
    }
  };

  const handleStatusChange = (ev: React.MouseEvent<HTMLElement>, checked?: boolean) => {
    const newStatus = isJobEnabled ? SyncStatus.CustomerPaused : SyncStatus.Idle;
    updateJobStatus(newStatus, SyncJobChangeReason.StatusUpdate);
  };

  const handleApproveSubmission = (approved: boolean) => {
    if (approved) {
      updateJobStatus(SyncStatus.Idle, SyncJobChangeReason.SubmissionApproved);
      resolveReview();
    } else {
      setShowRejectionDialog(true);
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
      await updateJobStatus(SyncStatus.SubmissionRejected, SyncJobChangeReason.SubmissionRejected, rejectionFeedback);
      resolveReview();
      setShowRejectionDialog(false);
      setRejectionFeedback('');
      setIsSubmittingRejection(false);
    } catch (error) {
      setIsSubmittingRejection(false);
      setRejectionError(strings.JobDetails.Errors.rejectionError);
    }
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
    <div className={classNames.membershipStatusContainer}>
      <div className={classNames.membershipStatusControls}>
        <label className={classNames.toggleLabel}>{strings.JobDetails.labels.sync}</label>
        <Toggle
          title={isJobEnabled ? strings.JobDetails.labels.enabled : strings.JobDetails.labels.disabled}
          inlineLabel={true}
          checked={isJobEnabled}
          onChange={handleStatusChange}
          disabled={!canEnableJob || jobStatus === SyncStatus.PendingReview || jobStatus === SyncStatus.SubmissionRejected}
        />
        <div>
          <div className={isJobEnabled ? classNames.jobEnabled : classNames.jobDisabled}>
            {isJobEnabled ? strings.JobDetails.labels.enabled : strings.JobDetails.labels.disabled}
          </div>
        </div>
      </div>
      <div className={classNames.membershipStatusMessage}>
        <div>
          {!patchResponse?.ok && (displayMessage(patchResponse))}
        </div>
        {(jobStatus === SyncStatus.PendingReview) && (
          <Stack>
            <div className={classNames.membershipStatusPendingLabel}>
              <Icon iconName='AlarmClock' className={classNames.clockIcon} />
              <Text>{strings.JobDetails.labels.pendingReview}</Text>
            </div>
            <Text>{isSubmissionReviewer ?
            <>
              {strings.JobDetails.labels.pendingReviewInstructions}
              {loadingJobChanges ? <Shimmer width="100%" /> :
              <>
              <Label>{strings.JobDetails.labels.businessJustification}</Label>
                {lastChange?.businessJustification}
              </>
              }
            </>
              : strings.JobDetails.labels.pendingReviewDescription}</Text>
            {isSubmissionReviewer && (
              <div className={classNames.membershipStatusActionButtons}>
                <DefaultButton onClick={() => handleApproveSubmission(true)} text={strings.JobDetails.labels.approve} />
                <PrimaryButton onClick={() => handleApproveSubmission(false)} text={strings.JobDetails.labels.reject} />
              </div>
            )}
          </Stack>
        )}
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
      {isSubmissionReviewer && (jobStatus === SyncStatus.PendingReview) && jobDetails && jobDetails.lastModifiedByObjectId && (
        <div>
        <Stack.Item align="start">
          <InfoLabel
            label={strings.JobDetails.labels.requestedBy}
            description={strings.JobDetails.descriptions.requestedBy}
          />
         <div className={classNames.itemData}>
          {jobDetails != null ? (
            (lastModifiedUserProfile?.photoUrl === "ErrorNonExistentStorage") ? (
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
        </Stack.Item>
        </div>
        )}

        {isSubmissionReviewer && (jobStatus === SyncStatus.PendingReview) && jobDetails && jobDetails.lastModifiedOnBehalfOfObjectId &&
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
    </div>
  )
}

const MembershipDestination: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const strings = useStrings();

  const { job, classNames } = props;

  const itemAlignmentsStackTokens: IStackTokens = {
    childrenGap: 30,
  };

  const mainStackTokens: IStackTokens = {
    childrenGap: 30,
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
}