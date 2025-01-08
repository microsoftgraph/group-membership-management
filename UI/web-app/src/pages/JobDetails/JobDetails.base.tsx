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
  DialogFooter
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
import { fetchJobDetails, getGroupDetails, patchJobDetails, removeGMM } from '../../store/jobDetails.api';
import {
  selectSelectedJobDetails,
  setGetJobDetailsError,
  selectGetJobDetailsError,
  selectPatchJobDetailsResponse,
  selectPatchJobDetailsError,
  selectRemoveGMMError,
  selectRemoveGMMLoading,
  selectSelectedJobLoading
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
import { SyncStatus } from '../../models';
import { OnboardingSteps } from '../../models/OnboardingSteps';
import { fetchJobs } from '../../store/jobs.api';
import { Loader } from '../../components/Loader';
import { setIsEditingExistingJob } from '../../store/manageMembership.slice';
import { SyncJobChangeReason } from '../../models/SyncJobChangeReason';
import { PatchJobRequest } from '../../models/PatchJobRequest';
import { MembershipConfiguration } from '../../components/MembershipConfiguration';
import { JobHistoryPanel } from '../../components/JobHistoryPanel/JobHistoryPanel';

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
  const [canEditJob, setCanEditJob] = useState<boolean>(isJobWriter && job?.status !== SyncStatus.PendingReview);
  const showLoader: boolean = jobLoading || removeGMMPending;

  const [isJobHistoryPanelOpen, setIsJobHistoryPanelOpen] = useState(false);
  
  const OpenInNewWindowIcon: IIconProps = { iconName: 'OpenInNewWindow' };

  const resolveReview = () => {
    setCanEditJob(isJobWriter);
  };

  const onMessageBarDismiss = (): void => {
    dispatch(setGetJobDetailsError());
  };

  const openInAzure = (): void => {
    var url = `https://ms.portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Overview/groupId/${job?.targetGroupId}`;
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
      var url = `https://portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Owners/${job?.targetGroupId}/menuId/`;
      window.open(url, '_blank', 'noopener,noreferrer');
      navigate('/');
    } catch (error) {
      setShowRemoveGMMError(true);
      throw new Error(`Failed to remove GMM: ${error}`);
    }
  };

  useEffect(() => {
    dispatch(setPagingBarVisible(false));
    if (jobId) {
      dispatch(fetchJobDetails({ syncJobId: jobId ?? '' }));
    }
    if (groupId) {
      dispatch(getGroupDetails(groupId));
    }
  }, [dispatch, jobId, groupId]);

  return (
    <Page>
      <PageHeader />
      {showLoader ? <Loader />
        : <>
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
                  { text: strings.JobDetails.openInAzure, icon: OpenInNewWindowIcon, onClick: openInAzure }
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
                  : [{ text: strings.JobDetails.viewDetails, icon: { iconName: 'View' }, onClick: openMembershipConfiguration }]
                }
                children={
                  <MembershipConfiguration 
                    isEditable={false}
                  />}
              />
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
            </div>
          )}
          {jobId && (
            <JobHistoryPanel
              isOpen={isJobHistoryPanelOpen}
              dismissPanel={() => setIsJobHistoryPanelOpen(false)}
              jobId={jobId}
            />
          )}
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
      }
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
          {strings.JobDetails.labels.pageTitle} - {props.job.targetGroupName}
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
  const [isJobEnabled, setIsJobEnabled] = useState(job.enabledOrNot);
  const isJobEnabler = useSelector(selectIsJobOwnerEnabler);
  const isJobWriter = useSelector(selectIsJobWriter);
  const canEnableJob = isJobEnabler || isJobWriter;

  useEffect(() => {
    setJobStatus(job?.status ?? '');
    setIsJobEnabled(job?.enabledOrNot ?? false);
  }, [job]);

  const updateJobStatus = async (newStatus: string, changeReason: SyncJobChangeReason) => {
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
      changeReason
    };

    try {
      await dispatch(patchJobDetails(patchRequest));

      if (patchResponse?.ok) {
        setJobStatus(newStatus);
        setIsJobEnabled(newStatus === SyncStatus.Idle);
      }
    } catch (error) {
      throw new Error('Failed to update job status');
    };
  };

  const handleStatusChange = (ev: React.MouseEvent<HTMLElement>, checked?: boolean) => {
    const newStatus = isJobEnabled ? SyncStatus.CustomerPaused : SyncStatus.Idle;
    updateJobStatus(newStatus, SyncJobChangeReason.StatusUpdate);
  };

  const handleApproveSubmission = (approved: boolean) => {
    const statusBasedOnReview = approved ? SyncStatus.Idle : SyncStatus.SubmissionRejected;
    updateJobStatus(statusBasedOnReview, approved ? SyncJobChangeReason.SubmissionApproved : SyncJobChangeReason.SubmissionRejected);
    resolveReview();
  };

  const displayMessage = (errorCode: string | undefined): string | undefined => {
    switch (errorCode) {
      case 'JobInProgress':
        return strings.JobDetails.Errors.jobInProgress;
      case 'Forbidden':
        return strings.JobDetails.Errors.forbidden;
      case 'InternalError':
        return strings.JobDetails.Errors.internalError;
      default:
        return undefined;
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
          {!patchResponse?.ok && (displayMessage(patchResponse?.errorCode ?? patchError))}
        </div>
        {(jobStatus === SyncStatus.PendingReview) && (
          <Stack>
            <div className={classNames.membershipStatusPendingLabel}>
              <Icon iconName='AlarmClock' className={classNames.clockIcon} />
              <Text>{strings.JobDetails.labels.pendingReview}</Text>
            </div>
            <Text>{isSubmissionReviewer ?
              strings.JobDetails.labels.pendingReviewInstructions
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
            <Text>{strings.JobDetails.labels.submissionRejected}</Text>
          </div>
        )}
      </div>
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

  const SharePointDomain: string = `${process.env.REACT_APP_SHAREPOINTDOMAIN}`;
  const domainName: string = `${process.env.REACT_APP_DOMAINNAME}`;
  const groupName: string = job?.targetGroupName?.replace(/\s/g, '');

  const openOutlookLink = (): void => {
    const url = `https://outlook.office.com/mail/group/${domainName}/${groupName}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const openSharePointLink = (): void => {
    const url = `https://${SharePointDomain}/sites/${groupName}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const openYammerLink = (): void => {
    const domainName: string = `${process.env.REACT_APP_DOMAINNAME}`
    const url = `https://www.yammer.com/${domainName}/groups/${groupName}`;
    window.open(url, '_blank', 'noopener,noreferrer');
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
              {job?.targetGroupType}
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
        </Stack>
      </Stack.Item>
      {job?.endpoints && (
        <Stack.Item align="start">
        {job.endpoints.every(endpoint => endpoint === 'SecurityGroup') ? (
          null
        ) : (
        <>
          <Text className={classNames.itemTitle} block>
            {strings.JobDetails.labels.groupLinks}
          </Text>
          <div className={classNames.itemData}>
            <Stack
              enableScopedSelectors
              horizontal
              tokens={itemAlignmentsStackTokens}
            >
              {job?.endpoints?.includes("Outlook") && (
                <ActionButton
                  iconProps={{ iconName: 'OutlookLogo' }}
                  onClick={() => openOutlookLink()}
                >
                  Outlook
                </ActionButton>
              )}
              {job?.endpoints?.includes("SharePoint") && (
                <ActionButton
                  iconProps={{ iconName: 'SharePointLogo' }}
                  onClick={() => openSharePointLink()}
                >
                  SharePoint
                </ActionButton>
              )}
              {job?.endpoints?.includes("Yammer") && (
                <ActionButton
                  iconProps={{ iconName: 'YammerLogo' }}
                  onClick={() => openYammerLink()}
                >
                  Yammer
                </ActionButton>
              )}
            </Stack>
          </div>
      </>
    )}
  </Stack.Item>
)}

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