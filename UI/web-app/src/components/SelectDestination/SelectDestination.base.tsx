// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState, useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
  Dropdown,
  IDropdownOption,
  Spinner,
  ActionButton,
  MessageBar,
  MessageBarType,
  MessageBarButton,
  NormalPeoplePicker,
  IPersonaProps
} from '@fluentui/react';
import {
  ISelectDestinationProps,
  ISelectDestinationStyleProps,
  ISelectDestinationStyles,
} from './SelectDestination.types';
import { useStrings } from '../../store/hooks';
import { PageSection } from '../PageSection';
import { AppDispatch } from '../../store';
import { searchDestinations } from '../../store/manageMembership.api';
import {
  manageMembershipSelectedDestinationEndpoints,
  manageMembershipSearchResults,
  manageMembershipLoadingSearchResults,
  manageMembershipGroupOnboardingStatus
} from '../../store/manageMembership.slice';
import { Destination } from '../../models/Destination';
import { selectOutlookWarningUrl } from '../../store/settings.slice';
import { OnboardingStatus } from '../../models';

const getClassNames = classNamesFunction<
  ISelectDestinationStyleProps,
  ISelectDestinationStyles
>();

export const SelectDestinationBase: React.FunctionComponent<ISelectDestinationProps> = (props) => {
  const { className, styles, selectedDestination, onSearchDestinationChange } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<ISelectDestinationStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  const optionsDestinationType: IDropdownOption[] = [
    { key: 'Group', text: 'Group' },
    { key: 'Channel', text: 'Channel', disabled: true }
  ];

  const mapDestinationToPersonaProps = (destination: Destination | undefined): IPersonaProps[] => {
    if (!destination) return [];

    return [{
      key: destination.id,
      text: destination.name,
    }];
  };

  const dispatch = useDispatch<AppDispatch>();
  const loadingSearchResults = useSelector(manageMembershipLoadingSearchResults);
  const onboardingStatus = useSelector(manageMembershipGroupOnboardingStatus);
  const selectedDestinationEndpoints = useSelector(manageMembershipSelectedDestinationEndpoints);
  const groupPickerSuggestions = useSelector(manageMembershipSearchResults);
  const selectedDestinationPersona = mapDestinationToPersonaProps(selectedDestination);
  const outlookWarningUrl = useSelector(selectOutlookWarningUrl);

  const [inputValue, setInputValue] = useState('');

  const debouncedSearch = useCallback(debounce((input: string) => {
    dispatch(searchDestinations(input));
  }, 300), []);

  const handleInputChange = (input: string): string => {
    setInputValue(input);
    debouncedSearch(input);
    return input;
  }

  const hasRequiredEndpoints = () => {
    if (!selectedDestinationEndpoints) return false;
    return ["Outlook", "Yammer", "SharePoint"].some(endpoint => selectedDestinationEndpoints.includes(endpoint));
  };

  const addGroupOwnerLink: string = `https://portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Owners/groupId/${selectedDestination?.id}/menuId/`

  const appIdNotOwnerWarning = onboardingStatus === OnboardingStatus.AppIdNotOwner ? (
    <div className={classNames.ownershipWarning}>
      {strings.ManageMembership.labels.appIdNotOwnerWarning} 
        <a href={addGroupOwnerLink} target="_blank" rel="noopener noreferrer">
          {strings.ManageMembership.labels.clickHere}
        </a>.
    </div>
  ) : null;

  const userNotOwnerWarning = onboardingStatus === OnboardingStatus.UserNotOwner ? (
    <div className={classNames.ownershipWarning}>
      {strings.ManageMembership.labels.userNotOwnerWarning}
    </div>
  ) : null;

  const alreadyOnboardedWarning = onboardingStatus === OnboardingStatus.Onboarded ? (
    <div className={classNames.ownershipWarning}>
      {strings.ManageMembership.labels.alreadyOnboardedWarning}
    </div>
  ) : null;

  const SharePointDomain: string = `${process.env.REACT_APP_SHAREPOINTDOMAIN}`;
  const domainName: string = `${process.env.REACT_APP_DOMAINNAME}`;
  const groupName: string | undefined = selectedDestination?.name.replace(/\s/g, '');

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

  const onClickOutlookWarning = (): void => {
    window.open(outlookWarningUrl, '_blank', 'noopener,noreferrer');
  };

  useEffect(() => {
  }, [dispatch, groupPickerSuggestions]);

  const getPickerSuggestions = async (
    text: string,
    currentGroups: IPersonaProps[] | undefined
  ): Promise<IPersonaProps[]> => {
    return text && groupPickerSuggestions ? groupPickerSuggestions : [];
  };

  return (
    <div className={classNames.root}>
      <PageSection>
        <div className={classNames.selectDestinationContainer}>
          <Dropdown
            placeholder={strings.ManageMembership.labels.selectDestinationTypePlaceholder}
            label={strings.ManageMembership.labels.selectDestinationType}
            options={optionsDestinationType}
            styles={{ title: classNames.dropdownTitle, dropdown: classNames.dropdownField }}
            required
            selectedKey={'Group'}
          />
          <div>
            {strings.ManageMembership.labels.searchDestination}
            <NormalPeoplePicker
              onResolveSuggestions={getPickerSuggestions}
              pickerSuggestionsProps={{
                suggestionsHeaderText: strings.ManageMembership.labels.searchGroupSuggestedText,
                noResultsFoundText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.noResultsFoundText,
                loadingText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.loadingText,
              }}
              key={'normal'}
              aria-label={strings.ManageMembership.labels.searchDestination}
              selectionAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.selectionAriaLabel}
              removeButtonAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.removeButtonAriaLabel}
              resolveDelay={300}
              itemLimit={1}
              selectedItems={selectedDestinationPersona}
              onInputChange={handleInputChange}
              onChange={onSearchDestinationChange}
              styles={{ text: classNames.peoplePicker }}
              pickerCalloutProps={{ calloutMinWidth: 500 }}
            />
          </div>
          <div className={classNames.resultsContainer}>
            {!hasRequiredEndpoints() && (
              <div className={classNames.spinnerContainer}>
                {loadingSearchResults ? <Spinner /> : null}
              </div>
            )}
            {selectedDestination && hasRequiredEndpoints() && (
              <div className={classNames.endpointsContainer}>
                {selectedDestinationEndpoints ? strings.ManageMembership.labels.appsUsed : ''}
                {selectedDestinationEndpoints?.includes("Outlook") && (
                  <div className={classNames.outlookContainer}>
                    <ActionButton
                      iconProps={{ iconName: 'OutlookLogo' }}
                      onClick={() => openOutlookLink()}
                    >
                      Outlook
                    </ActionButton>
                    {outlookWarningUrl &&
                      <MessageBar
                        messageBarType={MessageBarType.warning}
                        className={classNames.outlookWarning}
                        isMultiline={false}
                        actions={
                          <MessageBarButton onClick={() => onClickOutlookWarning()}>{strings.learnMore}</MessageBarButton>
                        }>
                        {strings.ManageMembership.labels.outlookWarning}
                      </MessageBar>}
                  </div>)}
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
                {appIdNotOwnerWarning}
                {userNotOwnerWarning}
                {alreadyOnboardedWarning}
              </div>
            )}
          </div>
        </div>
      </PageSection>
    </div>
  );
};

function debounce<T extends (...args: any[]) => void>(func: T, wait: number) {
  let timeout: NodeJS.Timeout;
  return function (this: ThisParameterType<T>, ...args: Parameters<T>) {
    clearTimeout(timeout);
    timeout = setTimeout(() => func.apply(this, args), wait);
  };
}