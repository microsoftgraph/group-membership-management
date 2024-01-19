// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import {
  IProcessedStyleSet,
  Stack,
  classNamesFunction,
  useTheme,
  Text,
  Separator,
  ActionButton
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
import { manageMembershipCompositeQuery, manageMembershipIsAdvancedView, manageMembershipPeriod, manageMembershipQuery, manageMembershipSelectedDestination, manageMembershipSelectedDestinationEndpoints, manageMembershipStartDate, manageMembershipThresholdPercentageForAdditions, manageMembershipThresholdPercentageForRemovals } from '../../store/manageMembership.slice';

const getClassNames = classNamesFunction<
  IConfirmationStyleProps,
  IConfirmationStyles
>();

export const ConfirmationBase: React.FunctionComponent<IConfirmationProps> = (props) => {
  const { 
    className, 
    styles,
    onEditButtonClick
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
  const isAdvancedView = useSelector(manageMembershipIsAdvancedView);
  const compositeQuery = useSelector(manageMembershipCompositeQuery);
  const globalQuery = useSelector(manageMembershipQuery);
  const displayQuery = isAdvancedView ? globalQuery : compositeQuery;

  const SharePointDomain: string = `${process.env.REACT_APP_SHAREPOINTDOMAIN}`;
  const domainName: string = `${process.env.REACT_APP_DOMAINNAME}`;
  const groupName: string | undefined = selectedDestination?.name.replace(/\s/g, '');

  const hasRequiredEndpoints = () => {
    if (!selectedDestinationEndpoints) return false;
    return ["Outlook", "Yammer", "SharePoint"].some(endpoint => selectedDestinationEndpoints.includes(endpoint));
  };

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
    <div className={classNames.root}>
      <PageSection>
        <div className={classNames.ConfirmationContainer}>

          <div>
          <div className={classNames.cardHeader}>
              <div className={classNames.cardTitle}>
                {strings.JobDetails.labels.destination}
              </div>
              <ActionButton 
                iconProps={{ iconName: 'Edit' }} 
                styles={{ root: { fontSize: 12, height: 14 }, icon: { fontSize: 10 }}}
                onClick={() => onEditButtonClick(1)}>
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
              {selectedDestination && hasRequiredEndpoints() ? (
              <Stack.Item align="start">
                <Text className={classNames.itemTitle} block>
                {selectedDestinationEndpoints ? strings.ManageMembership.labels.appsUsed : ''}
                </Text>
                <Text className={classNames.itemData} block>
                <div className={classNames.endpointsContainer}>
                  {selectedDestinationEndpoints?.includes("Outlook") && (
                    <ActionButton
                      iconProps={{ iconName: 'OutlookLogo' }}
                      onClick={() => openOutlookLink()}
                    >
                      Outlook
                    </ActionButton>
                    )}
                  {selectedDestinationEndpoints?.includes("SharePoint") && (
                    <ActionButton
                      iconProps={{ iconName: 'SharePointLogo' }}
                      onClick={() => openSharePointLink()}
                    >
                      SharePoint
                    </ActionButton>
                  )}
                  {selectedDestinationEndpoints?.includes("Yammer") && (
                    <ActionButton
                      iconProps={{ iconName: 'YammerLogo' }}
                      onClick={() => openYammerLink()}
                    >
                      Yammer
                    </ActionButton>
                  )}
                </div>
                </Text>
              </Stack.Item>
              ) : null}
            </Stack>
          </div>

          <div>
            <div className={classNames.cardHeader}>
              <div className={classNames.cardTitle}>
                {strings.JobDetails.labels.configuration}
              </div>
              <ActionButton 
                iconProps={{ iconName: 'Edit' }} 
                styles={{ root: { fontSize: 12, height: 14 }, icon: { fontSize: 10 }}}
                onClick={() => onEditButtonClick(2)}>
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
                onClick={() => onEditButtonClick(3)}>
                {strings.edit}
              </ActionButton>
            </div>
            <Separator />
            <Stack enableScopedSelectors tokens={{ childrenGap: 30 }}>
                <Stack.Item align="start">
                <pre>{displayQuery}</pre>
                </Stack.Item>
            </Stack>
          </div>
        </div>

      </PageSection>
    </div>
  );
};
