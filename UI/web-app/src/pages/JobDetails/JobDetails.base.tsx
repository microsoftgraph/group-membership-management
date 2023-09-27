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
  ActionButton
} from '@fluentui/react';

import {
  Stack,
  type IStackTokens
} from '@fluentui/react/lib/Stack';
import React, { useEffect } from 'react';
import { useSelector, useDispatch } from 'react-redux';
import { useLocation } from 'react-router-dom';
import { InfoLabel } from '../../components/InfoLabel';
import { PageHeader } from '../../components/PageHeader';
import { type Job } from '../../models/Job';
import { type AppDispatch } from '../../store';
import { fetchJobDetails } from '../../store/jobDetails.api';
import {
  selectSelectedJobDetails,
  setGetJobDetailsError,
  selectGetJobDetailsError,
} from '../../store/jobs.slice';

import { ContentContainer } from '../../components/ContentContainer/ContentContainer'
import { useTheme } from '@fluentui/react/lib/Theme';
import { Page } from '../../components/Page';
import { format } from 'react-string-format';
import {
  type IJobDetailsProps,
  type IJobDetailsStyleProps,
  type IJobDetailsStyles,
} from './JobDetails.types';
import { JobDetails } from '../../models/JobDetails';
import { PageVersion } from '../../components/PageVersion';
import { useStrings } from '../../localization/hooks';


export interface IContentProps extends React.AllHTMLAttributes<HTMLDivElement> {
  job: Job,
  jobDetails?: JobDetails,
  classNames: IProcessedStyleSet<IJobDetailsStyles>
}

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
  const job: Job = location.state.item;

  const dispatch = useDispatch<AppDispatch>();
  const jobDetails = useSelector(selectSelectedJobDetails);
  const error = useSelector(selectGetJobDetailsError);

  const OpenInNewWindowIcon: IIconProps = { iconName: 'OpenInNewWindow' };

  const onMessageBarDismiss = (): void => {
    dispatch(setGetJobDetailsError());
  };

  const openInAzure = (): void => {
    var url = `https://ms.portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Overview/groupId/${job.targetGroupId}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  useEffect(() => {
    dispatch(
      fetchJobDetails({
        syncJobId: job.syncJobId
      })
    );
  }, [dispatch]);

  return (
    <Page>
      <PageHeader />
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
      </div>
      <div className={classNames.root}>
        <MembershipDetails job={job} classNames={classNames} />
        {/* // Hidden until feature is enabled
        <ContentContainer
          title={strings.JobDetails.labels.membershipStatus}
          children={<MembershipStatusContent job={job} classNames={classNames} />}
          removeButton={true}
        /> */}
        <ContentContainer
          title={strings.JobDetails.labels.destination}
          actionText={strings.JobDetails.openInAzure}
          actionIcon={OpenInNewWindowIcon}
          actionOnClick={openInAzure}
          children={<MembershipDestination job={job} jobDetails={jobDetails} classNames={classNames} />}
        />
        <ContentContainer
          title={strings.JobDetails.labels.configuration}
          children={<MembershipConfiguration job={job} classNames={classNames} />}
          removeButton={true}
        // Hidden until feature is enabled
        // actionText={strings.JobDetails.editButton}
        // useLinkButton={true}
        // linkButtonIconName='edit'
        />
        <ContentContainer
          title={strings.JobDetails.labels.sourceParts}
          children={<label>{jobDetails?.source}</label>}
          removeButton={true}
        // Hidden until feature is enabled
        // actionText={strings.JobDetails.editButton}
        // useLinkButton={true}
        // linkButtonIconName='edit'
        />
        <div className={classNames.footer}>
          <PageVersion />
        </div>
      </div>
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
          {strings.JobDetails.labels.pageTitle}
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

const MembershipStatusContent: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const strings = useStrings();
  const { job, classNames } = props;

  return (
    <div className={classNames.membershipStatus}>
      <label className={classNames.toggleLabel}>{strings.JobDetails.labels.sync}</label>
      <Toggle
        inlineLabel={true}
        checked={job.enabledOrNot === 'Enabled'}
      />
      <div>
        {job.enabledOrNot === 'Disabled' ? (
          <div className={classNames.jobDisabled}> {job.enabledOrNot}</div>
        ) : (
          <div className={classNames.jobEnabled}> {job.enabledOrNot}</div>
        )}
      </div>
    </div>
  )
}

const MembershipDestination: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const strings = useStrings();
  const { job, jobDetails, classNames } = props;

  const itemAlignmentsStackTokens: IStackTokens = {
    childrenGap: 30,
  };

  const mainStackTokens: IStackTokens = {
    childrenGap: 30,
  };

  const SharePointDomain: string = `${process.env.REACT_APP_SHAREPOINTDOMAIN}`;
  const domainName: string = `${process.env.REACT_APP_DOMAINNAME}`;
  const groupName: string = job.targetGroupName.replace(/\s/g, '');

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
              {job.targetGroupType}
            </Text>
          </Stack.Item>

          <Stack.Item align="start">
            <Text className={classNames.itemTitle} block>
              {strings.JobDetails.labels.name}
            </Text>
            <Text className={classNames.itemData} block>
              {job.targetGroupName ?? '-'}
            </Text>
          </Stack.Item>

          <Stack.Item align="start">
            <InfoLabel
              label={strings.JobDetails.labels.ID}
              description={strings.JobDetails.descriptions.id}
            />
            <Text className={classNames.itemData} block>
              {job.targetGroupId}
            </Text>
          </Stack.Item>
        </Stack>
      </Stack.Item>
      {jobDetails?.endpoints ? (
        <Stack.Item align="start">
          <Text className={classNames.itemTitle} block>
            {strings.JobDetails.labels.groupLinks}
          </Text>
          <div className={classNames.itemData}>
            <Stack
              enableScopedSelectors
              horizontal
              tokens={itemAlignmentsStackTokens}
            >
              {jobDetails?.endpoints?.includes("Outlook") && (
                <ActionButton
                  iconProps={{ iconName: 'OutlookLogo' }}
                  onClick={() => openOutlookLink()}
                >
                  Outlook
                </ActionButton>
              )}
              {jobDetails?.endpoints?.includes("SharePoint") && (
                <ActionButton
                  iconProps={{ iconName: 'SharePointLogo' }}
                  onClick={() => openSharePointLink()}
                >
                  SharePoint
                </ActionButton>
              )}
              {jobDetails?.endpoints?.includes("Yammer") && (
                <ActionButton
                  iconProps={{ iconName: 'YammerLogo' }}
                  onClick={() => openYammerLink()}
                >
                  Yammer
                </ActionButton>
              )}
            </Stack>
          </div>
        </Stack.Item>
      ) : null}

    </Stack>
  )
}

const MembershipConfiguration: React.FunctionComponent<IContentProps> = (
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

  function splitDateString(value: string) {
    const spaceIndex = value.indexOf(' ');
    const isEmpty = value === '';
    const date = isEmpty
      ? '-'
      : value.substring(0, spaceIndex);
    const hoursMessage = isEmpty
      ? ''
      : value.substring(spaceIndex + 1);

    return [date, hoursMessage]
  }

  const lastRunDetails = splitDateString(job.lastSuccessfulRunTime);
  const nextRunDetails = splitDateString(job.estimatedNextRunTime);

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
              {new Intl.DateTimeFormat().format(Date.parse(jobDetails.lastSuccessfulStartTime))}
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
              {jobDetails.thresholdPercentageForAdditions}%
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
              {jobDetails.thresholdPercentageForRemovals}%
            </Text>
          ) : (
            <Shimmer width="100%" />
          )}
        </div>
      </Stack.Item>
    </Stack>
  )
}