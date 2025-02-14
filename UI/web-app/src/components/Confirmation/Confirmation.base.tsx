// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React  from 'react';
import { useCallback } from 'react';
import {
  IProcessedStyleSet,
  Stack,
  classNamesFunction,
  useTheme,
  Text,
  TextField,
  Separator,
  ActionButton,
} from '@fluentui/react';
import { format } from 'react-string-format';
import {
  IConfirmationProps,
  IConfirmationStyleProps,
  IConfirmationStyles,
} from './Confirmation.types';
import { useStrings } from "../../store/hooks";
import { PageSection } from "../PageSection";
import { useSelector } from 'react-redux';
import { 
  manageMembershipCompositeQuery,
  manageMembershipIsAdvancedView,
  manageMembershipPeriod,
  manageMembershipQuery,
  manageMembershipRequestor,
  manageMembershipSelectedDestination,
  manageMembershipSelectedDestinationEndpoints,
  manageMembershipStartDate,
  manageMembershipThresholdPercentageForAdditions,
  manageMembershipThresholdPercentageForRemovals,
  manageMembershipBusinessJustification
} from '../../store/manageMembership.slice';
import { OnboardingSteps } from '../../models/OnboardingSteps';
import { useLocation, useParams } from 'react-router-dom';
import { selectIsJobTenantWriter } from '../../store/roles.slice';
import { EndpointsList } from '../EndpointsList';
import { selectIsBusinessJustificationRequired } from '../../store/settings.slice';
import { debounce } from '../../utils/jobUtils';

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
  
  const selectedDestinationEndpoints = useSelector(manageMembershipSelectedDestinationEndpoints);
  const selectedDestination = useSelector(manageMembershipSelectedDestination);
  const period: number = useSelector(manageMembershipPeriod);
  const startDate: string = useSelector(manageMembershipStartDate);
  const thresholdPercentageForAdditions: number = useSelector(manageMembershipThresholdPercentageForAdditions);
  const thresholdPercentageForRemovals: number = useSelector(manageMembershipThresholdPercentageForRemovals);
  const requestor: string = useSelector(manageMembershipRequestor);
  const isBusinessJustificationRequired = useSelector(selectIsBusinessJustificationRequired);
  const businessJustification = useSelector(manageMembershipBusinessJustification);
  
  const isAdvancedView = useSelector(manageMembershipIsAdvancedView);
  const compositeQuery = useSelector(manageMembershipCompositeQuery);
  const globalQuery = useSelector(manageMembershipQuery);

  const isJobTenantWriter = useSelector(selectIsJobTenantWriter);

  const displayQuery: string = isAdvancedView ? JSON.stringify(globalQuery, null, 2) : JSON.stringify(compositeQuery, null, 2);

  const location = useLocation();
  const locationState = location.state as { currentStep?: number, jobId?: string };
  const { jobId: urlJobId } = useParams<{ jobId: string }>();
  const jobId = locationState?.jobId ?? urlJobId;

  
  const debouncedOnEditBusinessJustification = useCallback(
    debounce((newValue) => onEditBusinessJustification(newValue ?? ''), 300),
    []
  );

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
                    {selectedDestination?.type ?? '-'}
                  </Text>
                </Stack.Item>
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {strings.JobDetails.labels.name}
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
              </Stack>
              {selectedDestination && selectedDestinationEndpoints &&
                <EndpointsList 
                  endpoints={selectedDestinationEndpoints}
                  groupName={selectedDestination.name}
                />
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
              {isJobTenantWriter &&
                <Stack.Item align="start">
                  <Text className={classNames.itemTitle} block>
                    {strings.JobDetails.labels.requestor}
                  </Text>
                  <Text className={classNames.itemData} block>
                    {requestor}
                  </Text>
                </Stack.Item>
              }
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
                onChange={(_event, newValue) => debouncedOnEditBusinessJustification(newValue ?? '')}
                placeholder={strings.ManageMembership.labels.businessJustificationPlaceholder}
              />
            </div>
        </div>
      </PageSection>
    </div>
  );
};
