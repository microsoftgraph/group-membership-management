// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  Separator,
  Text,
  Shimmer,
  MessageBar,
  MessageBarType,
  IIconProps,
} from '@fluentui/react';
import {
  Stack,
  type IStackStyles,
  type IStackTokens,
  type IStackItemStyles,
} from '@fluentui/react/lib/Stack';
import React, { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useSelector, useDispatch } from 'react-redux';
import { useLocation } from 'react-router-dom';
import { ActionButton } from '@fluentui/react/lib/Button';

import { InfoLabel } from '../components/InfoLabel';
import { Page } from '../components/Page';
import { PageHeader } from '../components/PageHeader';
import { type Job } from '../models/Job';
import { type AppDispatch } from '../store';
import { fetchJobDetails } from '../store/jobDetails.api';
import {
  selectSelectedJobDetails,
  setGetJobDetailsError,
  selectGetJobDetailsError,
} from '../store/jobs.slice';

const titleStackItemStyles: IStackItemStyles = {
  root: {
    fontSize: 15,
    fontWeight: 'bold',
  },
};

const itemAlignmentsStackTokens: IStackTokens = {
  childrenGap: 30,
  padding: 15,
};

const stackStyles: Partial<IStackStyles> = { root: { height: 44 } };

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
        partitionKey: job.partitionKey,
        rowKey: job.rowKey,
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

      <Text variant="xxLarge" block>
        {t('JobDetailsPage.labels.pageTitle')}
      </Text>

      <div>
        <Stack
          enableScopedSelectors
          styles={stackStyles}
          tokens={itemAlignmentsStackTokens}
        >
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <Text variant="mediumPlus" block>
              {t('JobDetailsPage.labels.sectionTitle')}
            </Text>
            <Separator />
          </Stack.Item>

          {/* Last Modified by */}
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <InfoLabel
              label={t('JobDetailsPage.labels.lastModifiedby')}
              description={t('JobDetailsPage.descriptions.lastModifiedby')}
            />
            <Text variant="medium" block>
              DATA UNAVAILABLE
            </Text>
          </Stack.Item>

          {/* Group Links */}
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <Text variant="mediumPlus" block>
              {t('JobDetailsPage.labels.groupLinks')}
            </Text>
            <Text variant="medium" block>
              DATA UNAVAILABLE
            </Text>
          </Stack.Item>

          {/* Destination */}
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <Text variant="mediumPlus" block>
              {t('JobDetailsPage.labels.destination')}
            </Text>

            <Stack
              enableScopedSelectors
              tokens={itemAlignmentsStackTokens}
              horizontal
            >
              <Stack.Item align="start" styles={titleStackItemStyles}>
                <InfoLabel
                  label={t('JobDetailsPage.labels.type')}
                  description={t('JobDetailsPage.descriptions.type')}
                />
                <Text variant="medium" block>
                  {job.targetGroupType}
                </Text>
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <Text variant="mediumPlus" block>
                  {t('JobDetailsPage.labels.name')}
                </Text>
                <Text variant="medium" block>
                  {job.targetGroupName}
                </Text>
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <InfoLabel
                  label={t('JobDetailsPage.labels.ID')}
                  description={t('JobDetailsPage.descriptions.id')}
                />
                <Text variant="medium" block>
                  {job.targetGroupId}
                </Text>
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <ActionButton
                  iconProps={OpenInNewWindowIcon}
                  allowDisabledFocus
                  onClick={openInAzure}
                >
                  {t('JobDetailsPage.openInAzure')}
                </ActionButton>
              </Stack.Item>
            </Stack>
          </Stack.Item>

          {/* Configuration */}
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <Text variant="mediumPlus" block>
              {t('JobDetailsPage.labels.configuration')}
            </Text>

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
                {jobDetails != null ? (
                  <Text variant="medium" block>
                    {jobDetails.lastSuccessfulStartTime}
                  </Text>
                ) : (
                  <Shimmer width="120%" />
                )}
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <InfoLabel
                  label={t('JobDetailsPage.labels.endDate')}
                  description={t('JobDetailsPage.descriptions.endDate')}
                />
                <Text variant="medium" block>
                  DATA UNAVAILABLE
                </Text>
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <InfoLabel
                  label={t('JobDetailsPage.labels.lastRun')}
                  description={t('JobDetailsPage.descriptions.lastRun')}
                />
                <Text variant="medium" block>
                  {job.lastSuccessfulRunTime}
                </Text>
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <InfoLabel
                  label={t('JobDetailsPage.labels.nextRun')}
                  description={t('JobDetailsPage.descriptions.nextRun')}
                />
                <Text variant="medium" block>
                  {job.estimatedNextRunTime}
                </Text>
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <InfoLabel
                  label={t('JobDetailsPage.labels.frequency')}
                  description={t('JobDetailsPage.descriptions.frequency')}
                />
                {jobDetails != null ? (
                  <Text variant="medium" block>
                    {job.period}
                  </Text>
                ) : (
                  <Shimmer width="120%" />
                )}
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <InfoLabel
                  label={t('JobDetailsPage.labels.requestor')}
                  description={t('JobDetailsPage.descriptions.requestor')}
                />
                {jobDetails != null ? (
                  <Text variant="medium" block>
                    {jobDetails.requestor}
                  </Text>
                ) : (
                  <Shimmer width="120%" />
                )}
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <InfoLabel
                  label={t('JobDetailsPage.labels.increaseThreshold')}
                  description={t(
                    'JobDetailsPage.descriptions.increaseThreshold'
                  )}
                />
                {jobDetails != null ? (
                  <Text variant="medium" block>
                    {jobDetails.thresholdPercentageForAdditions}
                  </Text>
                ) : (
                  <Shimmer width="120%" />
                )}
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <InfoLabel
                  label={t('JobDetailsPage.labels.decreaseThreshold')}
                  description={t(
                    'JobDetailsPage.descriptions.decreaseThreshold'
                  )}
                />
                {jobDetails != null ? (
                  <Text variant="medium" block>
                    {jobDetails.thresholdPercentageForRemovals}
                  </Text>
                ) : (
                  <Shimmer width="120%" />
                )}
              </Stack.Item>

              <Stack.Item align="start" styles={titleStackItemStyles}>
                <InfoLabel
                  label={t('JobDetailsPage.labels.thresholdViolations')}
                  description={t(
                    'JobDetailsPage.descriptions.thresholdViolations'
                  )}
                />
                {jobDetails != null ? (
                  <Text variant="medium" block>
                    {jobDetails.thresholdViolations}
                  </Text>
                ) : (
                  <Shimmer width="120%" />
                )}
              </Stack.Item>
            </Stack>
          </Stack.Item>

          {/* Source Parts */}
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <Text variant="mediumPlus" block>
              {t('JobDetailsPage.labels.sourceParts')}
            </Text>
            <Separator />
            {jobDetails != null ? (
              <Text variant="medium" block>
                {jobDetails.source}
              </Text>
            ) : (
              <Shimmer width="120%" />
            )}
          </Stack.Item>
        </Stack>
      </div>
    </Page>
  );
};
