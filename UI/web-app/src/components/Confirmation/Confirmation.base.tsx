// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect } from 'react';
import {
  IProcessedStyleSet,
  Stack,
  classNamesFunction,
  useTheme,
  Text,
  TextField,
  Separator,
  ActionButton,
  IPersonaSharedProps,
  Persona,
  PersonaSize,
  Shimmer,
  Dropdown,
  IDropdownOption,
  MessageBar,
  MessageBarType,
  Icon
} from '@fluentui/react';
import { format } from 'react-string-format';
import {
  IConfirmationProps,
  IConfirmationStyleProps,
  IConfirmationStyles,
} from './Confirmation.types';
import { useStrings } from '../../store/hooks';
import { PageSection } from '../PageSection';
import { useDispatch, useSelector } from 'react-redux';
import {
  manageMembershipPeriod,
  manageMembershipSelectedDestination,
  manageMembershipSelectedDestinationEndpoints,
  manageMembershipStartDate,
  manageMembershipThresholdPercentageForAdditions,
  manageMembershipThresholdPercentageForRemovals,
  manageMembershipBusinessJustification,
  manageMembershipLastModifiedOnBehalfOfDisplayName,
  setNewJobLastModifiedOnBehalfOfDisplayName,
  setNewJobLastModifiedOnBehalfOfObjectId,
  manageMembershipGroupOwners,
  manageMembershipIsEditingExistingJob,
} from '../../store/manageMembership.slice';
import { OnboardingSteps } from '../../models/OnboardingSteps';
import { useLocation, useParams } from 'react-router-dom';
import { selectIsJobTenantWriter, selectIsJobWriter } from '../../store/roles.slice';
import { EndpointsList } from '../EndpointsList';
import { selectIsBusinessJustificationRequired } from '../../store/settings.slice';
import { InfoLabel } from '../InfoLabel';
import { selectSelectedJobDetails } from '../../store/jobs.slice';
import { selectLastModifiedOnBehalfOfUserProfile } from '../../store/profile.slice';
import { SyncStatus } from '../../models';
import { AppDispatch } from '../../store';
import { getGroupOwners } from '../../store/manageMembership.api';
import { SourcePartType } from '../../models/SourcePartType';
import { MembershipConfiguration } from '../MembershipConfiguration';
import { destinationTypeLocalization } from '../../utils/destinationTypeUtils';
import { Destination } from '../../models/Destination';
import { DestinationType } from '../../models/DestinationType';

const getClassNames = classNamesFunction<
  IConfirmationStyleProps,
  IConfirmationStyles
>();

export const ConfirmationBase: React.FunctionComponent<IConfirmationProps> = (props) => {
  const {
    className,
    styles,
    onEditButtonClick,
    onEditBusinessJustification
  } = props;
  const strings = useStrings();

  const classNames: IProcessedStyleSet<IConfirmationStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  const dispatch = useDispatch<AppDispatch>();
  const selectedDestinationEndpoints = useSelector(manageMembershipSelectedDestinationEndpoints);
  const selectedDestination = useSelector(manageMembershipSelectedDestination);
  const period: number = useSelector(manageMembershipPeriod);
  const startDate: string = useSelector(manageMembershipStartDate);
  const thresholdPercentageForAdditions: number = useSelector(manageMembershipThresholdPercentageForAdditions);
  const thresholdPercentageForRemovals: number = useSelector(manageMembershipThresholdPercentageForRemovals);
  const lastModifiedOnBehalfOfDisplayName = useSelector(manageMembershipLastModifiedOnBehalfOfDisplayName);
  const isBusinessJustificationRequired = useSelector(selectIsBusinessJustificationRequired);
  const businessJustification = useSelector(manageMembershipBusinessJustification);
  const jobDetails = useSelector(selectSelectedJobDetails);
  const lastModifiedOnBehalfOfUserProfile = useSelector(selectLastModifiedOnBehalfOfUserProfile);
  const groupOwners = useSelector(manageMembershipGroupOwners);
  const hasHiddenMembershipSources = jobDetails?.hasHiddenMembershipSources ?? false;
  const lastModifiedOnBehalfOfUserProps: IPersonaSharedProps = {
    imageUrl: lastModifiedOnBehalfOfUserProfile?.photoUrl,
    text: lastModifiedOnBehalfOfUserProfile?.displayName
  };

  const isJobTenantWriter = useSelector(selectIsJobTenantWriter);

  const location = useLocation();
  const locationState = location.state as { currentStep?: number, jobId?: string };
  const { jobId: urlJobId } = useParams<{ jobId: string }>();
  const jobId = locationState?.jobId ?? urlJobId;

  const isJobWriter = useSelector(selectIsJobWriter);
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);

  // When editing an existing job, the selectedDestination is not always populated in state,
  // so fall back to the destination data on the loaded job details so the DESTINATION section
  // still renders on the Confirmation step.
  const effectiveDestination: Destination | undefined = React.useMemo(() => {
    if (selectedDestination) {
      return selectedDestination;
    }
    if (isEditingExistingJob && jobDetails?.targetGroupId) {
      return {
        id: jobDetails.targetGroupId,
        name: jobDetails.targetGroupName,
        type: jobDetails.targetDestinationType || DestinationType.GroupMembership,
        channelId: jobDetails.targetChannelId,
        channelName: jobDetails.targetChannelName,
        endpoints: jobDetails.endpoints,
      };
    }
    return undefined;
  }, [selectedDestination, isEditingExistingJob, jobDetails]);

  const effectiveEndpoints = selectedDestinationEndpoints ?? effectiveDestination?.endpoints;

  // Fetch group owners when we have a destination (covers both new and edited jobs)
  useEffect(() => {
    if (effectiveDestination?.id) {
      dispatch(getGroupOwners(effectiveDestination.id));
    }
  }, [dispatch, effectiveDestination?.id]);

  // Create dropdown options from group owners
  const groupOwnerOptions: IDropdownOption[] = React.useMemo(() => {
    if (!groupOwners || !Array.isArray(groupOwners) || groupOwners.length === 0) {
      return [];
    }
    return groupOwners.map(owner => ({
      key: owner.objectId,
      text: owner.displayName,
      data: owner
    }));
  }, [groupOwners]);
    // Custom render function for dropdown options to display the email
  const onRenderOption = (option?: IDropdownOption): JSX.Element => {
    return option ? (
      <div className={classNames.dropdownOptionContainer}>
        <div>{option.text}</div>
        <div className={classNames.dropdownOptionEmail}>
          {option.data?.mail}
        </div>
      </div>
    ) : <></>;
  };
  // Handle group owner selection
  const handleGroupOwnerChange = (event: React.FormEvent<HTMLDivElement>, option?: IDropdownOption) => {
    if (option) {
      const selectedOwner = option.data;
      dispatch(setNewJobLastModifiedOnBehalfOfDisplayName(selectedOwner.displayName));
      dispatch(setNewJobLastModifiedOnBehalfOfObjectId(selectedOwner.objectId));
    } else {
      dispatch(setNewJobLastModifiedOnBehalfOfDisplayName(''));
      dispatch(setNewJobLastModifiedOnBehalfOfObjectId(''));
    }
  };

  const isTeamsDestination = effectiveDestination?.type === SourcePartType.TeamsChannelMembership;

  const openInService = (): void => {
    let url: string | undefined;
    if (isTeamsDestination) {
      url = `https://teams.microsoft.com/l/channel/${effectiveDestination?.channelId}`;
    } else if (effectiveDestination?.id) {
      url = `https://ms.portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Overview/groupId/${effectiveDestination.id}`;
    }
    if (url) {
      window.open(url, '_blank', 'noopener,noreferrer');
    }
  };

  const SQL_MIN_DATE = new Date('1753-01-01T00:00:00');
  const formatRunDate = (value?: string): string => {
    if (!value) {
      return '-';
    }
    const parsed = new Date(value);
    if (isNaN(parsed.getTime()) || parsed <= SQL_MIN_DATE) {
      return '-';
    }
    return new Intl.DateTimeFormat().format(parsed);
  };

  return (
    <div className={classNames.root}>
      <PageSection>
        <div className={classNames.ConfirmationContainer}>

        {effectiveDestination && (<div>
          <div className={classNames.cardHeader}>
              <div className={classNames.cardTitle}>
                {strings.JobDetails.labels.destination}
              </div>
              {(isTeamsDestination || effectiveDestination?.id) &&
                <ActionButton
                  iconProps={{ iconName: 'OpenInNewWindow' }}
                  styles={{ root: { fontSize: 12, height: 14 }, icon: { fontSize: 10 }}}
                  onClick={openInService}>
                  {isTeamsDestination ? strings.JobDetails.openInTeams : strings.JobDetails.openInAzure}
                </ActionButton>
              }
            </div>
            <Stack enableScopedSelectors tokens={{ childrenGap: 30 }}>
              <Stack horizontal tokens={{ childrenGap: 100 }}>
                <Stack.Item align="start">
                  <InfoLabel
                    label={strings.JobDetails.labels.type}
                    description={strings.JobDetails.descriptions.type}
                  />
                  <Text className={classNames.itemData} block>
                  {destinationTypeLocalization[effectiveDestination!.type] || effectiveDestination?.type}
                  </Text>
                </Stack.Item>
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {isTeamsDestination ? strings.JobDetails.labels.teamName : strings.JobDetails.labels.name}
                  </Text>
                  <Text className={classNames.itemData} block>
                    {effectiveDestination?.name ?? '-'}
                  </Text>
                </Stack.Item>
                <Stack.Item align="start">
                  <InfoLabel
                    label={strings.ManageMembership.labels.objectId}
                    description={strings.JobDetails.descriptions.id}
                  />
                  <Text className={classNames.itemData} block>
                    {effectiveDestination?.id ?? '-'}
                  </Text>
                </Stack.Item>
                {effectiveDestination?.channelName &&
                  <Stack.Item align="start">
                    <Text className={classNames.itemTitle} block>
                      {strings.JobDetails.labels.channelName}
                    </Text>
                    <Text className={classNames.itemData} block>
                      {effectiveDestination?.channelName ?? '-'}
                    </Text>
                  </Stack.Item>
                }
                {effectiveDestination?.channelId &&
                  <Stack.Item align="start">
                    <Text className={classNames.itemTitle} block>
                      {strings.JobDetails.labels.channelId}
                    </Text>
                    <Text className={classNames.itemData} block>
                      {effectiveDestination?.channelId ?? '-'}
                    </Text>
                  </Stack.Item>
                }
              </Stack>
              {effectiveDestination && effectiveEndpoints &&
                <EndpointsList
                  endpoints={effectiveEndpoints}
                  groupName={effectiveDestination.name}
                  linksTitle={strings.JobDetails.labels.groupLinks}
                  horizontal={true}
                />
              }
              {effectiveDestination && effectiveDestination.groupSettings &&
                effectiveDestination.groupSettings.authorizedSenders && effectiveDestination.groupSettings.authorizedSenders.length > 0 &&
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {strings.ManageMembership.CreateGroup.authorizedSenders}
                  </Text>
                  <Text className={classNames.itemData} block>
                    {effectiveDestination.groupSettings.authorizedSenders.map((sender) => sender.mail).join(', ')}
                  </Text>
                </Stack.Item>
              }
              {effectiveDestination && effectiveDestination.groupSettings &&
                effectiveDestination.groupSettings.hiddenFromExchangeClients &&
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {strings.ManageMembership.CreateGroup.hiddenFromExchangeClients}
                  </Text>
                  <Text className={classNames.itemData} block>
                    {effectiveDestination.groupSettings.hiddenFromExchangeClients ? strings.yes : strings.no}
                  </Text>
                </Stack.Item>
              }
              {effectiveDestination && effectiveDestination.groupSettings &&
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {strings.ManageMembership.CreateGroup.welcomeMessageEnabled}
                  </Text>
                  <Text className={classNames.itemData} block>
                    {effectiveDestination.groupSettings.welcomeMessageEnabled ? strings.yes : strings.no}
                  </Text>
                </Stack.Item>
              }
            </Stack>
          </div>)}

            <div>
              <Separator />
              <div className={classNames.cardHeader}>
                <div className={classNames.cardTitle}>
                  {strings.JobDetails.labels.businessJustification}
                </div>
              </div>
              <TextField
                multiline
                resizable={true}
                autoAdjustHeight
                required={isBusinessJustificationRequired}
                onRenderLabel={() => (
                  <div className={classNames.businessJustificationLabel}>
                    <Text className={classNames.itemTitle} block>
                      {`${strings.ManageMembership.labels.businessJustificationSubtitle} ${strings.ManageMembership.labels.businessJustificationPrompt}`}
                    </Text>
                    <div className={classNames.businessJustificationHelper}>
                      <Icon iconName="Info" aria-hidden="true" />
                      <Text block>
                        {strings.ManageMembership.labels.businessJustificationHelperText}
                      </Text>
                    </div>
                  </div>
                )}
                contentEditable={false}
                value={businessJustification}
                onChange={(_event, newValue) => onEditBusinessJustification(newValue ?? '')}
                placeholder={strings.ManageMembership.labels.businessJustificationPlaceholder}
                data-testid="business-justification-textarea"
              />
              {hasHiddenMembershipSources && (
                <MessageBar
                  messageBarType={MessageBarType.warning}
                  isMultiline={false}
                >
                  {strings.ManageMembership.labels.hiddenMembershipConfirmationWarning}
                </MessageBar>
              )}

            {isJobTenantWriter && (
            jobId && jobDetails && jobDetails?.status === SyncStatus.PendingReview && jobDetails.lastModifiedOnBehalfOfObjectId? (
            jobDetails && jobDetails?.status === SyncStatus.PendingReview ? (
              <Stack.Item align="start">
                <InfoLabel
                  label={strings.ManageMembership.labels.requestedOnBehalfOf}
                  description={strings.JobDetails.descriptions.requestedOnBehalfOf}
                />
                <div className={classNames.itemData}>
                  {jobDetails != null ? (
                    lastModifiedOnBehalfOfUserProfile?.photoUrl === "ErrorNonExistentStorage" ? (
                      <div className={classNames.itemData}>
                        <Text variant="medium" block>
                          {lastModifiedOnBehalfOfUserProfile.displayName}
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
            ) : null
          ) : (
            <>
              <Separator />
              <Dropdown
                id="groupOwnersDropdown"
                label={strings.ManageMembership.labels.requestedOnBehalfOfDescription}
                aria-label={strings.ManageMembership.labels.requestedOnBehalfOf}
                placeholder={strings.ManageMembership.labels.requestedOnBehalfOfPlaceholder}
                options={groupOwnerOptions}
                selectedKey={lastModifiedOnBehalfOfDisplayName && groupOwners ?
                  groupOwners.find(owner => owner.displayName === lastModifiedOnBehalfOfDisplayName)?.objectId :
                  undefined}
                onChange={handleGroupOwnerChange}
                disabled={!isJobWriter || groupOwnerOptions.length === 0}
                styles={{
                  dropdown: classNames.valuesDropdown,
                  title: classNames.dropdownTitle
                }}
                onRenderOption={onRenderOption}
                required
                data-testid="group-owners-dropdown"
              />
            </>
          ))}
            </div>

          <div>
            <Separator />
            <div className={classNames.cardHeader}>
              <div className={classNames.cardTitle}>
                {strings.JobDetails.labels.configuration}
              </div>
              <ActionButton
                iconProps={{ iconName: 'Edit' }}
                styles={{ root: { fontSize: 12, height: 14 }, icon: { fontSize: 10 }}}
                onClick={() => onEditButtonClick(OnboardingSteps.RunConfiguration)}>
                {strings.edit}
              </ActionButton>
            </div>
            <Stack horizontal tokens={{ childrenGap: 40 }}>
              <Stack.Item align="start">
                <InfoLabel
                  label={strings.JobDetails.labels.startDate}
                  description={strings.JobDetails.descriptions.startDate}
                />
                <Text className={classNames.itemData} block>
                  {new Date(startDate) <= SQL_MIN_DATE
                    ? strings.ManageMembership.labels.ASAP
                    : new Intl.DateTimeFormat().format(Date.parse(startDate))}
                </Text>
              </Stack.Item>
              {jobDetails && (
                <Stack.Item align="start">
                  <InfoLabel
                    label={strings.JobDetails.labels.lastRun}
                    description={strings.JobDetails.descriptions.lastRun}
                  />
                  <Text className={classNames.itemData} block>
                    {formatRunDate(jobDetails.lastSuccessfulRunTime)}
                  </Text>
                </Stack.Item>
              )}
              {jobDetails && (
                <Stack.Item align="start">
                  <InfoLabel
                    label={strings.JobDetails.labels.nextRun}
                    description={strings.JobDetails.descriptions.nextRun}
                  />
                  <Text className={classNames.itemData} block>
                    {formatRunDate(jobDetails.estimatedNextRunTime)}
                  </Text>
                </Stack.Item>
              )}
              <Stack.Item align="start">
                <InfoLabel
                  label={strings.JobDetails.labels.frequency}
                  description={strings.JobDetails.descriptions.frequency}
                />
                <Text className={classNames.itemData} block>
                  {format(strings.JobDetails.labels.frequencyDescription, period)}
                </Text>
              </Stack.Item>
              <Stack.Item align="start">
                <InfoLabel
                  label={strings.JobDetails.labels.increaseThreshold}
                  description={strings.JobDetails.descriptions.increaseThreshold}
                />
                <Text className={classNames.itemData} block>
                  {thresholdPercentageForAdditions === -1 ? `${strings.ManageMembership.labels.noThresholdSet}`: `${thresholdPercentageForAdditions}%`}
                </Text>
              </Stack.Item>
              <Stack.Item align="start">
                <InfoLabel
                  label={strings.JobDetails.labels.decreaseThreshold}
                  description={strings.JobDetails.descriptions.decreaseThreshold}
                />
                <Text className={classNames.itemData} block>
                  {thresholdPercentageForRemovals === -1 ? `${strings.ManageMembership.labels.noThresholdSet}`: `${thresholdPercentageForRemovals}%`}
                </Text>
              </Stack.Item>
            </Stack>
          </div>

          <div>
            <Separator />
            <div className={classNames.cardHeader}>
                <div className={classNames.cardTitle}>
                {strings.JobDetails.labels.sourceParts}
                </div>
                <ActionButton
                  iconProps={{ iconName: 'Edit' }}
                  styles={{ root: { fontSize: 12, height: 14 }, icon: { fontSize: 10 }}}
                  onClick={() => onEditButtonClick(OnboardingSteps.MembershipConfiguration)}>
                  {strings.edit}
                </ActionButton>
              </div>
              <MembershipConfiguration isEditable={false} />
            </div>

        </div>
      </PageSection>
    </div>
  );
};
