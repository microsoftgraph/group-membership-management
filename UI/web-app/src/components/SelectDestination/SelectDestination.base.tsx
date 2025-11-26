// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState, useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
  ComboBox,
  Spinner,
  NormalPeoplePicker,
  IPersonaProps,
  IComboBoxOption,
  ISelectableOption,
  Text,
  ChoiceGroup,
  IChoiceGroupOption,
  IIconProps,
  DefaultButton,
  IButtonStyles,
  MessageBar,
  MessageBarType,
} from '@fluentui/react';
import {
  ISelectDestinationProps,
  ISelectDestinationStyleProps,
  ISelectDestinationStyles,
} from './SelectDestination.types';
import { useStrings } from '../../store/hooks';
import { PageSection } from '../PageSection';
import { AppDispatch } from '../../store';
import { searchChannels, searchDestinations, getGroupOnboardingStatus, getChannelOnboardingStatus, getGroupMembers } from '../../store/manageMembership.api';
import {
  manageMembershipSelectedDestinationEndpoints,
  manageMembershipSearchResults,
  manageMembershipChannelPickerSearchResults,
  manageMembershipLoadingSearchResults,
  manageMembershipGroupOnboardingStatus,
  manageMembershipGroupMembers,
} from '../../store/manageMembership.slice';
import { Destination } from '../../models/Destination';
import { SearchChannelRequest } from '../../models/SearchChannelRequest';
import { selectCreateGroupFeatureEnabled } from '../../store/settings.slice';
import { OnboardingStatus } from '../../models';
import { EndpointsList } from '../EndpointsList';
import { CreateGroup } from '../CreateGroup';
import { debounce } from '../../utils/jobUtils';
import { selectIsJobTenantWriter } from '../../store/roles.slice';
import { SourcePartType } from '../../models/SourcePartType';
import { DestinationType } from '../../models/DestinationType';
import { GroupSetting } from '../GroupSetting/GroupSetting';
import { jsxFormat } from '../../utils/stringUtils';
import { ChannelOnboardingStatusRequest } from '../../models/ChannelOnboardingStatusRequest';

const getClassNames = classNamesFunction<ISelectDestinationStyleProps, ISelectDestinationStyles>();

export const SelectDestinationBase: React.FunctionComponent<ISelectDestinationProps> = (props) => {
  const { className, styles, selectedDestination, onDestinationTypeChange, onSearchDestinationChange, onSearchChannelChange, onGroupCreated } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<ISelectDestinationStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  const destinationActionOptions: IChoiceGroupOption[] = [
    {
      key: 'Create',
      text: strings.ManageMembership.labels.createNewGroup,
    },
    {
      key: 'Select',
      text: strings.ManageMembership.labels.selectDestination,
    },
  ];

  const onRenderValueComboBoxOptions = (
    props?: ISelectableOption,
    defaultRender?: (props?: ISelectableOption) => JSX.Element | null
  ): JSX.Element | null => {
    return (
      <div className={classNames.comboBoxOptionContainer}>
        <div>
          <Text>{props?.text}</Text>
        </div>
        <div>
          <Text variant="tiny" styles={{ root: classNames.comboBoxOptionCodeText }}>
            {props?.data?.description}
          </Text>
        </div>
      </div>
    );
  };
  const mapDestinationToType = (destination: Destination | undefined): string => {
    return destination?.type ?? DestinationType.GroupMembership;
  };
  const mapDestinationToChannelPersonaProps = (destination: Destination | undefined): IPersonaProps[] => {
    if (!destination) return [];

    if(!destination.channelId || !destination.channelName) {
      return [];
    }

    return [
      {
        key: destination.channelId,
        text: destination.channelName,
      },
    ];
  };
  const mapDestinationToPersonaProps = (destination: Destination | undefined): IPersonaProps[] => {
    if (!destination) return [];

    if(!destination.id || !destination.name) {
      return [];
    }

    return [
      {
        key: destination.id,
        text: destination.name,
      },
    ];
  };

  const dispatch = useDispatch<AppDispatch>();
  const loadingSearchResults = useSelector(manageMembershipLoadingSearchResults);
  const onboardingStatus = useSelector(manageMembershipGroupOnboardingStatus);
  const selectedDestinationEndpoints = useSelector(manageMembershipSelectedDestinationEndpoints);
  const groupPickerSuggestions = useSelector(manageMembershipSearchResults);
  const channelPickerSuggestions = useSelector(manageMembershipChannelPickerSearchResults);
  const groupMembers = useSelector(manageMembershipGroupMembers);
  const selectedDestinationType = mapDestinationToType(selectedDestination);
  const selectedDestinationChannelPersona = mapDestinationToChannelPersonaProps(selectedDestination);
  const selectedDestinationPersona = mapDestinationToPersonaProps(selectedDestination);
  const [createNewGroup, setCreateNewGroup] = useState(false);
  const isCreateGroupEnabled = useSelector(selectCreateGroupFeatureEnabled);
  const isTenantJobWriter: boolean | undefined = useSelector(selectIsJobTenantWriter);

  const optionsDestinationType: IComboBoxOption[] = [
    {
      key: DestinationType.GroupMembership,
      text: 'Group',
      data: {
        description: strings.ManageMembership.labels.groupDescription,
      },
    },
    {
      key: DestinationType.TeamsChannelMembership,
      text: 'Channel',
      data: {
        description: strings.ManageMembership.labels.channelDescription,
      },
      disabled: !isTenantJobWriter,
    },
  ];

  const debouncedSearch = useCallback(
    debounce((input: string) => {
      if(input != undefined && input.length > 0) {
        dispatch(searchDestinations(input));
      }
    }, 50),
    []
  );

  const debouncedChannelSearch = useCallback(
    debounce((input: string, currentDestination: Destination | undefined) => {
      if (input != undefined && input.length > 0) {
        const channelRequest: SearchChannelRequest = {
          teamId: currentDestination?.id ?? "",
          query: input,
        };
        dispatch(searchChannels(channelRequest));
      }
    }, 50),
    []
  );

  const handleInputChange = (input: string): string => {
    debouncedSearch(input);
    return input;
  };

  const handleChannelInputChange = (input: string): string => {
    debouncedChannelSearch(input, selectedDestination);
    return input;
  };

  const hasRequiredEndpoints = () => {
    if (!selectedDestinationEndpoints) return false;
    return ['Outlook', 'Yammer', 'SharePoint', 'SecurityGroup'].some((endpoint) =>
      selectedDestinationEndpoints.includes(endpoint)
    );
  };

  const addGroupOwnerLink: string = `https://portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Owners/groupId/${selectedDestination?.id}/menuId/`;

  const refreshIcon: IIconProps = { iconName: 'Refresh' };
  const smallButtonStyles: IButtonStyles = {
    root: {
      border: 'none',
      backgroundColor: 'transparent',
      padding: '0px 0px',
    },
  };

  const checkOwnership = (
  item?: any,
  index?: number,
  ev?: React.FocusEvent<HTMLElement>
  ): void => {
     if(selectedDestination?.id && selectedDestination?.type === DestinationType.GroupMembership){
      dispatch(getGroupOnboardingStatus(selectedDestination?.id));
     }
     else if(selectedDestination?.id && selectedDestination?.type === DestinationType.TeamsChannelMembership){
      const channelOnboardingStatusRequest: ChannelOnboardingStatusRequest = {
              teamId: selectedDestination.id!,
              channelId: selectedDestination.channelId!,
            };
      dispatch(getChannelOnboardingStatus(channelOnboardingStatusRequest));
     }
  };

  const appIdNotOwnerWarning =
    onboardingStatus?.status === OnboardingStatus.GmmNotOwner ? (
      <MessageBar
        messageBarType={MessageBarType.error}
        isMultiline={true}
        dismissButtonAriaLabel="Close"
        className={classNames.ownershipWarning}
      >
        <div>
          {selectedDestination?.type === DestinationType.TeamsChannelMembership ? (
            <div>
              <div>{strings.ManageMembership.labels.teamsServiceAccountNotOwnerWarning.split('{2}')[0]}</div>
              <div className={classNames.messageBarSection}>
                {jsxFormat(strings.ManageMembership.labels.addOwnerMessage, <a href={addGroupOwnerLink} target="_blank" rel="noopener noreferrer">{strings.ManageMembership.labels.here}</a>, onboardingStatus?.additionalDetails?.["owner"])}
              </div>
            </div>
          ) : (
            <div>
              <div>{strings.ManageMembership.labels.appIdNotOwnerWarning.split('{2}')[0]}</div>
              {selectedDestination?.type === DestinationType.GroupMembership && (
                <div className={classNames.messageBarSection}>
                  {jsxFormat(strings.ManageMembership.labels.addOwnerMessage, <a href={addGroupOwnerLink} target="_blank" rel="noopener noreferrer">{strings.ManageMembership.labels.here}</a>, onboardingStatus?.additionalDetails?.["owner"])}
                </div>
              )}
            </div>
          )}
        </div>
      </MessageBar>
    ) : null;

  const userNotOwnerWarning =
    onboardingStatus?.status === OnboardingStatus.UserNotOwner ? (
      <div className={classNames.ownershipWarning}>{strings.ManageMembership.labels.userNotOwnerWarning}</div>
    ) : null;

  const alreadyOnboardedWarning =
    onboardingStatus?.status === OnboardingStatus.Onboarded ? (
      <div className={classNames.ownershipWarning}>{strings.ManageMembership.labels.alreadyOnboardedWarning}</div>
    ) : null;

  const teamsNotSupportedWarning =
    onboardingStatus?.status == OnboardingStatus.ReadyForOnboarding && selectedDestination?.type === DestinationType.TeamsChannelMembership && !selectedDestinationEndpoints?.includes("Microsoft Teams") ? (
      <div className={classNames.ownershipWarning}>{strings.ManageMembership.labels.teamsNotSupportedWarning}</div>
    ) : null;

  const checkNestedGroups = (
    item?: any,
    index?: number,
    ev?: React.FocusEvent<HTMLElement>
  ): void => {
    if (selectedDestination?.id) {
      dispatch(getGroupMembers(selectedDestination.id));
    }
  };

  const hasNestedGroupsWarning =
    groupMembers && groupMembers.groupMemberCount > 0 ? (
      <MessageBar
        messageBarType={MessageBarType.error}
        isMultiline={true}
        dismissButtonAriaLabel="Close"
        className={classNames.ownershipWarning}
      >
        <div className={classNames.messageBarContent}>
          <div>{strings.ManageMembership.labels.hasNestedGroupsWarning}</div>
          {groupMembers.groups && groupMembers.groups.length > 0 && (
            <div className={classNames.messageBarSection}>
              <strong>{strings.ManageMembership.labels.nestedGroupsFound}</strong>
              <ul style={{ marginTop: '4px', marginBottom: '0' }}>
                {groupMembers.groups.slice(0, 5).map((group) => (
                  <li key={group.objectId}>{group.displayName}</li>
                ))}
              </ul>
              <div className={classNames.messageBarSection}>
                {jsxFormat(strings.ManageMembership.labels.viewInEntra, <a href={`https://portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Members/groupId/${selectedDestination?.id}`} target="_blank" rel="noopener noreferrer">{strings.ManageMembership.labels.here}</a>)}
              </div>
            </div>
          )}
        </div>
      </MessageBar>
    ) : null;

  useEffect(() => {}, [dispatch, groupPickerSuggestions]);

  const getPickerSuggestions = async (
    text: string,
    currentGroups: IPersonaProps[] | undefined
  ): Promise<IPersonaProps[]> => {
    return text && groupPickerSuggestions ? groupPickerSuggestions : [];
  };

  const getChannelPickerSuggestions = async (
    text: string,
    currentChannels: IPersonaProps[] | undefined
  ): Promise<IPersonaProps[]> => {
    return text && channelPickerSuggestions ? channelPickerSuggestions : [];
  };

  const visibleOptionsDestinationType = optionsDestinationType.filter((option) => !option.disabled);

  const onDestinationActionChange = (
    ev?: React.FormEvent<HTMLElement | HTMLInputElement>,
    option?: IChoiceGroupOption
  ): void => {
    if (option?.key === 'Create') {
      setCreateNewGroup(true);
    } else {
      setCreateNewGroup(false);
    }
  };

  return (
    <div className={classNames.root}>
      <PageSection>
        <div className={classNames.selectDestinationContainer}>
          {isCreateGroupEnabled && (
            <ChoiceGroup
              label={strings.ManageMembership.labels.selectOrCreateGroup}
              options={destinationActionOptions}
              onChange={(ev, option) => onDestinationActionChange(ev, option)}
              selectedKey={createNewGroup ? 'Create' : 'Select'}
            />
          )}
          {createNewGroup ? (
            <CreateGroup onGroupCreated={onGroupCreated} />
          ) : (
            <>
              <ComboBox
                placeholder={strings.ManageMembership.labels.selectDestinationTypePlaceholder}
                label={strings.ManageMembership.labels.selectDestinationType}
                options={visibleOptionsDestinationType}
                required
                selectedKey={selectedDestinationType}
                onChange={onDestinationTypeChange}
                onRenderOption={onRenderValueComboBoxOptions}
                styles={{ root: classNames.peoplePicker }}
              />
              <div>
                {selectedDestination?.type == DestinationType.TeamsChannelMembership ? strings.ManageMembership.labels.searchTeam: strings.ManageMembership.labels.searchGroup}
                <NormalPeoplePicker
                  onResolveSuggestions={getPickerSuggestions}
                  pickerSuggestionsProps={{
                    suggestionsHeaderText: strings.ManageMembership.labels.searchGroupSuggestedText,
                    noResultsFoundText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.noResultsFoundText,
                    loadingText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.loadingText,
                  }}
                  key={'normal'}
                  aria-label={strings.ManageMembership.labels.searchTeam}
                  selectionAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.selectionAriaLabel}
                  removeButtonAriaLabel={
                    strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.removeButtonAriaLabel
                  }
                  resolveDelay={600}
                  itemLimit={1}
                  selectedItems={selectedDestinationPersona}
                  onInputChange={handleInputChange}
                  onChange={onSearchDestinationChange}
                  styles={{ text: classNames.peoplePicker }}
                  pickerCalloutProps={{ calloutMinWidth: 500 }}
                />
              </div>
              {selectedDestination?.id != null && selectedDestinationType === DestinationType.TeamsChannelMembership && (
                <div>
                  {strings.ManageMembership.labels.searchChannel}
                  <NormalPeoplePicker
                    onResolveSuggestions={getChannelPickerSuggestions}
                    pickerSuggestionsProps={{
                      suggestionsHeaderText: strings.ManageMembership.labels.searchChannelSuggestedText,
                      noResultsFoundText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.noResultsFoundText,
                      loadingText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.loadingText,
                    }}
                    key={'normal'}
                    aria-label={selectedDestination?.type == SourcePartType.TeamsChannelMembership ? strings.ManageMembership.labels.searchTeam: strings.ManageMembership.labels.searchGroup}
                    selectionAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.selectionAriaLabel}
                    removeButtonAriaLabel={
                      strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.removeButtonAriaLabel
                    }
                    resolveDelay={600}
                    itemLimit={1}
                    selectedItems={selectedDestinationChannelPersona}
                    onInputChange={handleChannelInputChange}
                    onChange={onSearchChannelChange}
                    styles={{ text: classNames.peoplePicker }}
                    pickerCalloutProps={{ calloutMinWidth: 500 }}
                  />
                </div>
              )}
              <div className={classNames.resultsContainer}>
                {!hasRequiredEndpoints() && (
                  <div className={classNames.spinnerContainer}>{loadingSearchResults ? <Spinner /> : null}</div>
                )}
                {selectedDestination && selectedDestinationEndpoints && (
                  <EndpointsList
                    endpoints={selectedDestinationEndpoints}
                    groupName={selectedDestination.name}
                    showOutlookWarning={true}
                  />
                )}
                {appIdNotOwnerWarning}
                {userNotOwnerWarning}
                {alreadyOnboardedWarning}
                {teamsNotSupportedWarning}
                {hasNestedGroupsWarning}
                {(appIdNotOwnerWarning || hasNestedGroupsWarning) && (
                  <div>
                    {jsxFormat(
                      strings.ManageMembership.labels.refreshInstructions,
                      <DefaultButton 
                        text={strings.ManageMembership.labels.checkAgain} 
                        title={strings.ManageMembership.labels.checkAgain}
                        styles={smallButtonStyles}
                        onClick={() => {
                          if (appIdNotOwnerWarning) checkOwnership();
                          if (hasNestedGroupsWarning) checkNestedGroups();
                        }} 
                      />
                    )}
                  </div>
                )}
              </div>
              {selectedDestination && selectedDestination.groupSettings && (
                <GroupSetting />
              )}
            </>
          )}
        </div>
      </PageSection>
    </div>
  );
};
