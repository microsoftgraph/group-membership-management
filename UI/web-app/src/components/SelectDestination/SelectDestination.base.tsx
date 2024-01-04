// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
  Dropdown, IDropdownOption,
  Spinner,
  ActionButton,
  MessageBar, MessageBarType, MessageBarButton, NormalPeoplePicker, IPersonaProps
} from '@fluentui/react';
import {
  ISelectDestinationProps,
  ISelectDestinationStyleProps,
  ISelectDestinationStyles,
} from './SelectDestination.types';
import { useStrings } from "../../store/hooks";
import { PageSection } from "../PageSection";
import { AppDispatch } from '../../store';
import { searchDestinations } from '../../store/manageMembership.api';
import {
  manageMembershipSelectedDestinationEndpoints,
  manageMembershipSearchResults,
  manageMembershipIsGroupReadyForOnboarding,
  manageMembershipLoadingSearchResults
} from '../../store/manageMembership.slice';
import { Destination } from '../../models/Destination';
import { selectOutlookWarningUrl } from '../../store/settings.slice';

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
  const isReadyForOnboarding = useSelector(manageMembershipIsGroupReadyForOnboarding);
  const selectedDestinationEndpoints = useSelector(manageMembershipSelectedDestinationEndpoints);
  const groupPickerSuggestions = useSelector(manageMembershipSearchResults);
  const selectedDestinationPersona = mapDestinationToPersonaProps(selectedDestination);
  const outlookWarningUrl = useSelector(selectOutlookWarningUrl);

  const hasRequiredEndpoints = () => {
    if (!selectedDestinationEndpoints) return false;
    return ["Outlook", "Yammer", "SharePoint"].some(endpoint => selectedDestinationEndpoints.includes(endpoint));
  };

  const addGroupOwnerLink: string = `https://portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Owners/groupId/${selectedDestination?.id}/menuId/`
  const ownershipWarning = isReadyForOnboarding === false ? (
    <div className={classNames.ownershipWarning}>
      {strings.ManageMembership.labels.ownershipWarning} <a href={addGroupOwnerLink}>{strings.clickHere}</a>.
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

  const handleDestinationInputChanged = (input: string): string => {
    dispatch(searchDestinations(input));
    return input;
  }

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
            selectedKey={selectedDestination?.type}
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
              selectionAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.selectionAriaLabel}
              removeButtonAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.removeButtonAriaLabel}
              resolveDelay={300}
              itemLimit={1}
              selectedItems={selectedDestinationPersona}
              onInputChange={handleDestinationInputChanged}
              onChange={onSearchDestinationChange}
              styles={{ text: classNames.peoplePicker }}
              pickerCalloutProps={{ calloutMinWidth: 500 }}
            />
          </div>
          {loadingSearchResults ? (
            <Spinner />
          ) : (
            <div>
              {selectedDestination && hasRequiredEndpoints() ? (
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
                  {ownershipWarning}
                </div>
              ) : null}
            </div>
          )}
        </div>
      </PageSection>
    </div>
  );
};
