// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
  Dropdown, IDropdownOption,
  ComboBox, IComboBoxOption,
  Spinner,
  ActionButton,
  MessageBar, MessageBarType, MessageBarButton,
} from '@fluentui/react';
import {
  ISelectDestinationProps,
  ISelectDestinationStyleProps,
  ISelectDestinationStyles,
} from './SelectDestination.types';
import { useStrings } from "../../localization";
import { PageSection } from "../PageSection";
import { AppDispatch } from '../../store';
import { searchDestinations } from '../../store/manageMembership.api';
import { 
  manageMembershipSelectedDestinationEndpoints, 
  manageMembershipSearchResults, 
  manageMembershipIsGroupReadyForOnboarding, 
  manageMembershipLoadingSearchResults} from '../../store/manageMembership.slice';

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

  const dispatch = useDispatch<AppDispatch>();
  const searchResults = useSelector(manageMembershipSearchResults);
  const loadingSearchResults = useSelector(manageMembershipLoadingSearchResults);
  const [searchQuery, setSearchQuery] = useState('');
  const isReadyForOnboarding = useSelector(manageMembershipIsGroupReadyForOnboarding);
  const selectedDestinationEndpoints = useSelector(manageMembershipSelectedDestinationEndpoints);

  const debounce = (func: (...args: any[]) => void, delay: number) => {
    let timeout: NodeJS.Timeout;
    return (...args: any[]) => {
      clearTimeout(timeout);
      timeout = setTimeout(() => func(...args), delay);
    };
  };

  const handleSearch = debounce(() => {
    if (searchQuery.length > 3) {
      dispatch(searchDestinations(searchQuery));
    }
  }, 200);

  const hasRequiredEndpoints = () => {
    if (!selectedDestinationEndpoints) return false;
    return ["Outlook", "Yammer", "SharePoint"].some(endpoint => selectedDestinationEndpoints.includes(endpoint));
  };

  // const outlookWarningUrl = useSelector((state: RootState) => selectSelectedSetting(state, 'outlookWarningUrl'));
  // useEffect(() => {
  //   dispatch(fetchSettingByKey('outlookWarningUrl'));
  // }, [dispatch]);

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
    // window.open(outlookWarningUrl?.value, '_blank', 'noopener,noreferrer');
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
            selectedKey={selectedDestination?.type}
          />
          <div>
            {strings.ManageMembership.labels.searchDestination}
            <ComboBox
              styles={{
                root: classNames.searchField,
                container: classNames.comboBoxContainer,
                input: classNames.comboBoxInput
              }}
              placeholder={strings.ManageMembership.labels.searchDestinationPlaceholder}
              useComboBoxAsMenuWidth
              options={searchResults?.map(group => ({ key: group.id, text: group.name })) || []}
              required
              allowFreeform
              onChange={onSearchDestinationChange}
              selectedKey={selectedDestination?.id}
              onPendingValueChanged={(option?: IComboBoxOption, index?: number, value?: string) => {
                if (value !== undefined) {
                  setSearchQuery(value);
                  handleSearch();
                }
              }}
            />
            {
              searchQuery.length > 3 && !loadingSearchResults && (!searchResults || searchResults.length === 0) ? (
                <div>
                  {strings.ManageMembership.labels.noResultsFound}
                </div>
              ) : null
            }
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
                      <MessageBar
                        messageBarType={MessageBarType.warning}
                        className={classNames.outlookWarning}
                        isMultiline={false}
                        actions={
                          <MessageBarButton onClick={() => onClickOutlookWarning()}>{strings.learnMore}</MessageBarButton>
                        }>
                        {strings.ManageMembership.labels.outlookWarning}
                      </MessageBar>
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
