// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  Text,
  Shimmer,
  MessageBar,
  MessageBarType,
  IIconProps,
  Toggle
} from '@fluentui/react';

import {
  Stack,
  type IStackTokens,
  type IStackItemStyles,
} from '@fluentui/react/lib/Stack';
import React, { useEffect } from 'react';
import { useTranslation, } from 'react-i18next';
import { useSelector, useDispatch } from 'react-redux';
import { useLocation } from 'react-router-dom';
import { InfoLabel } from '../components/InfoLabel';
import { PageHeader } from '../components/PageHeader';
import { type Job } from '../models/Job';
import { type AppDispatch } from '../store';
import { fetchJobDetails } from '../store/jobDetails.api';
import {
  selectSelectedJobDetails,
  setGetJobDetailsError,
  selectGetJobDetailsError,
} from '../store/jobs.slice';

import { ContentContainer } from '../components/ContentContainer/ContentContainer'
import { useTheme } from '@fluentui/react/lib/Theme';
import { Page } from '../components/Page';
import { format } from 'react-string-format';

const itemData: React.CSSProperties = {
  paddingTop: 10
}

export interface IContentProps extends React.AllHTMLAttributes<HTMLDivElement> {
  job: Job
}

const MembershipDetails: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const { t } = useTranslation();
  const theme = useTheme();
  const card: React.CSSProperties = {
    paddingTop: 18,
    paddingBottom: 18,
    paddingLeft: 22,
    paddingRight: 22,
    borderRadius: 10,
    marginBottom: 12,
    backgroundColor: theme.palette.white,
  }
  const subtitle: React.CSSProperties = {
    fontWeight: "bold",
    display: "block",
    marginTop: 10
  }

  return (
    <div style={card}>
      <div>
        <Text variant="xxLarge" block>
          {t('JobDetailsPage.labels.pageTitle')}
        </Text>
      </div>
      <div>
        <Text style={subtitle}>
          {t('JobDetailsPage.labels.lastModifiedby')}
          <Text variant="medium" style={{ marginLeft: 5 }}>
            {'DATA UNAVAILABLE'}
          </Text>
        </Text>
      </div>
    </div>
  )
}

const MembershipStatusContent: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const { t } = useTranslation();
  const theme = useTheme();
  const toggleLabel: React.CSSProperties = {
    paddingRight: 10
  }
  const jobEnabled: React.CSSProperties = {
    color: theme.palette.black,
    backgroundColor: theme.semanticColors.successBackground,
    borderRadius: 50,
    textAlign: 'center',
    height: 20,
    paddingLeft: 5,
    paddingRight: 5,
    marginLeft: 15
  }
  const jobDisabled: React.CSSProperties = {
    color: theme.palette.black,
    backgroundColor: theme.palette.themeLighterAlt,
    borderRadius: 50,
    textAlign: 'center',
    height: 20,
    paddingLeft: 5,
    paddingRight: 5,
    marginLeft: 15
  }
  const { job } = props;



  return (
    <div style={{ display: "flex", alignItems: "flex-start" }}>
      <label style={toggleLabel}>{t('JobDetailsPage.labels.sync')}</label>
      <Toggle
        inlineLabel={true}
        checked={job.enabledOrNot === 'Enabled'}
      />
      <div>
        {job.enabledOrNot === 'Disabled' ? (
          <div style={jobDisabled}> {job.enabledOrNot}</div>
        ) : (
          <div style={jobEnabled}> {job.enabledOrNot}</div>
        )}
      </div>
    </div>
  )
}

const MembershipDestination: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const { t } = useTranslation();
  const { job } = props;

  const titleStyle: React.CSSProperties = {
    fontWeight: 'bold'
  }

  const titleStackItemStyles: IStackItemStyles = {
    root: {
      fontSize: 15,
      fontWeight: 'bold'
    },
  };

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
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <InfoLabel
              label={t('JobDetailsPage.labels.type')}
              description={t('JobDetailsPage.descriptions.type')}
            />
            <Text style={itemData} variant="medium" block>
              {job.targetGroupType}
            </Text>
          </Stack.Item>

          <Stack.Item align="start" styles={titleStackItemStyles}>
            <Text style={titleStyle} variant="mediumPlus" block>
              {t('JobDetailsPage.labels.name')}
            </Text>
            <Text style={itemData} variant="medium" block>
              {job.targetGroupName}
            </Text>
          </Stack.Item>

          <Stack.Item align="start" styles={titleStackItemStyles}>
            <InfoLabel
              label={t('JobDetailsPage.labels.ID')}
              description={t('JobDetailsPage.descriptions.id')}
            />
            <Text style={itemData} variant="medium" block>
              {job.targetGroupId}
            </Text>
          </Stack.Item>
        </Stack>
      </Stack.Item>
      <Stack.Item align="start" styles={titleStackItemStyles}>
        <Text style={titleStyle} variant="mediumPlus" block>
          {t('JobDetailsPage.labels.groupLinks')}
        </Text>
        <Text style={itemData} variant="medium" block>
        </Text>
      </Stack.Item>
    </Stack>
  )
}

const MembershipConfiguration: React.FunctionComponent<IContentProps> = (
  props: IContentProps
) => {
  const { t } = useTranslation();
  const { job } = props;
  const jobDetails = useSelector(selectSelectedJobDetails);

  const titleStackItemStyles: IStackItemStyles = {
    root: {
      fontSize: 15,
      fontWeight: 'bold'
    },
  };

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
      <Stack.Item align="start" styles={titleStackItemStyles}>
        <InfoLabel
          label={t('JobDetailsPage.labels.startDate')}
          description={t('JobDetailsPage.descriptions.startDate')}
        />
        <div style={itemData}>
          {jobDetails != null ? (
            <Text variant="medium" block>
              {new Intl.DateTimeFormat().format(Date.parse(jobDetails.lastSuccessfulStartTime))}
            </Text>
          ) : (
            <Shimmer width="120%" />
          )}
        </div>
      </Stack.Item>

      <Stack.Item align="start" styles={titleStackItemStyles}>
        <InfoLabel
          label={t('JobDetailsPage.labels.endDate')}
          description={t('JobDetailsPage.descriptions.endDate')}
        />
        <div style={itemData}>
          <Text variant="medium" block>
            00/00/0000
          </Text>
        </div>
      </Stack.Item>

      <Stack.Item align="start" styles={titleStackItemStyles}>
        <InfoLabel
          label={t('JobDetailsPage.labels.lastRun')}
          description={t('JobDetailsPage.descriptions.lastRun')}
        />
        <div style={itemData}>
          <Text variant="medium" block>
            {lastRunDetails[0]}
          </Text>
          <Text style={hoursMessage} variant="medium" block>
            {lastRunDetails[1]}
          </Text>
        </div>
      </Stack.Item>

      <Stack.Item align="start" styles={titleStackItemStyles}>
        <InfoLabel
          label={t('JobDetailsPage.labels.nextRun')}
          description={t('JobDetailsPage.descriptions.nextRun')}
        />
        <div style={itemData}>
          <Text variant="medium" block>
            {nextRunDetails[0]}
          </Text>
          <Text style={hoursMessage} variant="medium" block>
            {nextRunDetails[1]}
          </Text>
        </div>
      </Stack.Item>

      <Stack.Item align="start" styles={titleStackItemStyles}>
        <InfoLabel
          label={t('JobDetailsPage.labels.frequency')}
          description={t('JobDetailsPage.descriptions.frequency')}
        />
        <div style={itemData}>
          {jobDetails != null ? (
            <Text variant="medium" block>
              {format(t('JobDetailsPage.labels.frequencyDescription'), job.period)}
            </Text>
          ) : (
            <Shimmer width="120%" />
          )}
        </div>
      </Stack.Item>

      <Stack.Item align="start" styles={titleStackItemStyles}>
        <InfoLabel
          label={t('JobDetailsPage.labels.increaseThreshold')}
          description={t(
            'JobDetailsPage.descriptions.increaseThreshold'
          )}
        />
        <div style={itemData}>
          {jobDetails != null ? (
            <Text variant="medium" block>
              {jobDetails.thresholdPercentageForAdditions}%
            </Text>
          ) : (
            <Shimmer width="120%" />
          )}
        </div>
      </Stack.Item>

      <Stack.Item align="start" styles={titleStackItemStyles}>
        <InfoLabel
          label={t('JobDetailsPage.labels.decreaseThreshold')}
          description={t(
            'JobDetailsPage.descriptions.decreaseThreshold'
          )}
        />
        <div style={itemData}>
          {jobDetails != null ? (
            <Text variant="medium" block>
              {jobDetails.thresholdPercentageForRemovals}%
            </Text>
          ) : (
            <Shimmer width="120%" />
          )}
        </div>
      </Stack.Item>
    </Stack>
  )
}

export const JobDetailsPage: React.FunctionComponent = () => {
  const { t } = useTranslation();
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
              t('JobDetailsPage.MessageBar.dismissButtonAriaLabel') as string
            }
          >
            {error}
          </MessageBar>
        )}
      </div>

      <div>

        <MembershipDetails job={job} />

        <ContentContainer
          title={t('JobDetailsPage.labels.membershipStatus')}
          children={<MembershipStatusContent job={job} />}
          removeButton={true}
        />

        <ContentContainer
          title={t('JobDetailsPage.labels.destination')}
          actionText={t('JobDetailsPage.openInAzure')}
          actionIcon={OpenInNewWindowIcon}
          actionOnClick={openInAzure}
          children={<MembershipDestination job={job} />}
        />

        <ContentContainer
          title={t('JobDetailsPage.labels.configuration')}
          actionText={t('JobDetailsPage.editButton')}
          useLinkButton={true}
          linkButtonIconName='edit'
          children={<MembershipConfiguration job={job} />}
        />

        <ContentContainer
          title={t('JobDetailsPage.labels.sourceParts')}
          actionText={t('JobDetailsPage.editButton')}
          useLinkButton={true}
          linkButtonIconName='edit'
          children={<label>{jobDetails?.source}</label>}
        />

      </div>
    </Page >
  );
};
