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
  Label,
  Dropdown,
  IDropdownOption
} from '@fluentui/react';
import { format } from 'react-string-format';
import {
  IConfirmationProps,
  IConfirmationStyleProps,
  IConfirmationStyles,
} from './Confirmation.types';
import { useStrings } from "../../store/hooks";
import { PageSection } from "../PageSection";
import { useDispatch, useSelector } from 'react-redux';
import {
  manageMembershipCompositeQuery,
  manageMembershipIsAdvancedView,
  manageMembershipPeriod,
  manageMembershipQuery,
  manageMembershipSelectedDestination,
  manageMembershipSelectedDestinationEndpoints,
  manageMembershipStartDate,
  manageMembershipThresholdPercentageForAdditions,
  manageMembershipThresholdPercentageForRemovals,
  manageMembershipBusinessJustification,
  manageMembershipLastModifiedOnBehalfOfDisplayName,
  setNewJobLastModifiedOnBehalfOfDisplayName,
  setNewJobLastModifiedOnBehalfOfObjectId,
  manageMembershipGroupOwners
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
import { destinationTypeLocalization } from '../../utils/destinationTypeUtils';

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
  const lastModifiedOnBehalfOfUserProps: IPersonaSharedProps = {
    imageUrl: lastModifiedOnBehalfOfUserProfile?.photoUrl,
    text: lastModifiedOnBehalfOfUserProfile?.displayName
  };

  const isAdvancedView = useSelector(manageMembershipIsAdvancedView);
  const compositeQuery = useSelector(manageMembershipCompositeQuery);
  const globalQuery = useSelector(manageMembershipQuery);

  const isJobTenantWriter = useSelector(selectIsJobTenantWriter);

  const displayQuery: string = isAdvancedView ? JSON.stringify(globalQuery, null, 2) : JSON.stringify(compositeQuery, null, 2);

  const location = useLocation();
  const locationState = location.state as { currentStep?: number, jobId?: string };
  const { jobId: urlJobId } = useParams<{ jobId: string }>();
  const jobId = locationState?.jobId ?? urlJobId;

  const isJobWriter = useSelector(selectIsJobWriter);
  // Fetch group owners when we have a selected destination
  useEffect(() => {
    if (selectedDestination?.id) {
      dispatch(getGroupOwners(selectedDestination.id));
    }
  }, [dispatch, selectedDestination?.id]);

  // Create dropdown options from group owners
  const groupOwnerOptions: IDropdownOption[] = React.useMemo(() => {
    if (!groupOwners || groupOwners.length === 0) {
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

  return (
    <div className={classNames.root}>
      <PageSection>
        <div className={classNames.ConfirmationContainer}>

        {!jobId && (<div>
          <div className={classNames.cardHeader}>
              <div className={classNames.cardTitle}>
                {strings.JobDetails.labels.destination}
              </div>
              <ActionButton 
                iconProps={{ iconName: 'Edit' }} 
                styles={{ root: { fontSize: 12, height: 14 }, icon: { fontSize: 10 }}}
                onClick={() => onEditButtonClick(OnboardingSteps.SelectDestination)}>
                {strings.edit}
              </ActionButton>
            </div>
            <Separator />
            <Stack enableScopedSelectors tokens={{ childrenGap: 30 }}>
              <Stack horizontal tokens={{ childrenGap: 30 }}>
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {strings.JobDetails.labels.type}
                  </Text>
                  <Text className={classNames.itemData} block>
                  {destinationTypeLocalization[selectedDestination!.type] || selectedDestination?.type}
                  </Text>
                </Stack.Item>
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {selectedDestination?.type === SourcePartType.TeamsChannelMembership ? strings.JobDetails.labels.teamName : strings.JobDetails.labels.name}
                  </Text>
                  <Text className={classNames.itemData} block>
                    {selectedDestination?.name ?? '-'}
                  </Text>
                </Stack.Item>
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {strings.ManageMembership.labels.objectId}
                  </Text>
                  <Text className={classNames.itemData} block>
                    {selectedDestination?.id ?? '-'}
                  </Text>
                </Stack.Item>
                {selectedDestination?.channelName &&
                  <Stack.Item align="start">
                    <Text className={classNames.itemTitle} block>
                      {strings.JobDetails.labels.channelName}
                    </Text>
                    <Text className={classNames.itemData} block>
                      {selectedDestination?.channelName ?? '-'}
                    </Text>
                  </Stack.Item> 
                }
                {selectedDestination?.channelId &&
                  <Stack.Item align="start">
                    <Text className={classNames.itemTitle} block>
                      {strings.JobDetails.labels.channelId}
                    </Text>
                    <Text className={classNames.itemData} block>
                      {selectedDestination?.channelId ?? '-'}
                    </Text>
                  </Stack.Item>
                }
              </Stack>
              {selectedDestination && selectedDestinationEndpoints &&
                <EndpointsList 
                  endpoints={selectedDestinationEndpoints}
                  groupName={selectedDestination.name}
                />
              }
              {selectedDestination && selectedDestination.groupSettings &&
                selectedDestination.groupSettings.authorizedSenders && selectedDestination.groupSettings.authorizedSenders.length > 0 &&
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {strings.ManageMembership.CreateGroup.authorizedSenders}
                  </Text>
                  <Text className={classNames.itemData} block>
                    {selectedDestination.groupSettings.authorizedSenders.map((sender) => sender.mail).join(', ')}
                  </Text>
                </Stack.Item>
              }
              {selectedDestination && selectedDestination.groupSettings &&
                selectedDestination.groupSettings.hiddenFromExchangeClients &&
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {strings.ManageMembership.CreateGroup.hiddenFromExchangeClients}
                  </Text>
                  <Text className={classNames.itemData} block>
                    {selectedDestination.groupSettings.hiddenFromExchangeClients ? strings.yes : strings.no}
                  </Text>
                </Stack.Item>
              }
              {selectedDestination && selectedDestination.groupSettings &&
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {strings.ManageMembership.CreateGroup.welcomeMessageEnabled}
                  </Text>
                  <Text className={classNames.itemData} block>
                    {selectedDestination.groupSettings.welcomeMessageEnabled ? strings.yes : strings.no}
                  </Text>
                </Stack.Item>
              }
            </Stack>
          </div>)}

          <div>
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
            <Separator />
            <Stack horizontal tokens={{ childrenGap: 15 }}>
              <Stack.Item align="start">
                <Text className={classNames.itemTitle} block>
                  {strings.JobDetails.labels.startDate}
                </Text>
                <Text className={classNames.itemData} block>
                  {new Intl.DateTimeFormat().format(Date.parse(startDate))}
                </Text>
              </Stack.Item>
              <Stack.Item align="start">
                <Text className={classNames.itemTitle} block>
                  {strings.JobDetails.labels.frequency}
                </Text>
                <Text className={classNames.itemData} block>
                  {format(strings.JobDetails.labels.frequencyDescription, period)}
                </Text>
              </Stack.Item>
              <Stack.Item align="start">
                <Text className={classNames.itemTitle} block>
                  {strings.JobDetails.labels.increaseThreshold}
                </Text>
                <Text className={classNames.itemData} block>
                  {thresholdPercentageForAdditions === -1 ? `${strings.ManageMembership.labels.noThresholdSet}`: `${thresholdPercentageForAdditions}%`}
                </Text>
              </Stack.Item>
              <Stack.Item align="start">
                <Text className={classNames.itemTitle} block>
                  {strings.JobDetails.labels.decreaseThreshold}
                </Text>
                <Text className={classNames.itemData} block>
                  {thresholdPercentageForRemovals === -1 ? `${strings.ManageMembership.labels.noThresholdSet}`: `${thresholdPercentageForRemovals}%`}
                </Text>
              </Stack.Item>
            </Stack>
          </div>

          <div>
            <div className={classNames.cardHeader}>
                <div className={classNames.cardTitle}>
                {strings.ManageMembership.labels.sourceParts}
                </div>
                <ActionButton 
                  iconProps={{ iconName: 'Edit' }} 
                  styles={{ root: { fontSize: 12, height: 14 }, icon: { fontSize: 10 }}}
                  onClick={() => onEditButtonClick(OnboardingSteps.MembershipConfiguration)}>
                  {strings.edit}
                </ActionButton>
              </div>
              <Separator />
              <Stack enableScopedSelectors tokens={{ childrenGap: 30 }}>
                <Stack.Item align="stretch" grow>
                  <TextField
                    value={displayQuery}
                    readOnly
                    multiline
                    resizable={true}
                    autoAdjustHeight={true}
                    styles={{
                      field: { fontFamily: "monospace" },
                    }}
                  />
                </Stack.Item>
              </Stack>
            </div>
        
            <div>
              <div className={classNames.cardHeader}>
                <div className={classNames.cardTitle}>
                  {strings.JobDetails.labels.businessJustification}
                </div>
              </div>
              <Separator />
              <TextField
                multiline
                resizable={true}
                autoAdjustHeight
                required={isBusinessJustificationRequired}
                label={`${strings.ManageMembership.labels.businessJustificationSubtitle} ${strings.ManageMembership.labels.businessJustificationPrompt}`}
                contentEditable={false}
                value={businessJustification}
                onChange={(_event, newValue) => onEditBusinessJustification(newValue ?? '')}
                placeholder={strings.ManageMembership.labels.businessJustificationPlaceholder}
              />
            </div>
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
            <div>
              <div className={classNames.cardHeader}>
                <div className={classNames.cardTitle}>                  
                  <Label>{strings.ManageMembership.labels.requestedOnBehalfOf}</Label>
                </div>
              </div>
              <Separator />
              <Dropdown
                id="groupOwnersDropdown"
                label={strings.ManageMembership.labels.requestedOnBehalfOfDescription}
                aria-label={strings.ManageMembership.labels.requestedOnBehalfOf}
                placeholder={strings.ManageMembership.labels.requestedOnBehalfOfPlaceholder}
                options={groupOwnerOptions}
                selectedKey={lastModifiedOnBehalfOfDisplayName ? 
                  groupOwners?.find(owner => owner.displayName === lastModifiedOnBehalfOfDisplayName)?.objectId : 
                  undefined}
                onChange={handleGroupOwnerChange}
                disabled={!isJobWriter || groupOwnerOptions.length === 0}
                styles={{ 
                  dropdown: classNames.valuesDropdown, 
                  title: classNames.dropdownTitle 
                }}
                onRenderOption={onRenderOption}
                required
              />
            </div>
          ))}

        </div>
      </PageSection>
    </div>
  );
};
