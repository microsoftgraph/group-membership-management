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
  manageMembershipGroupOnboardingStatus,
} from '../../store/manageMembership.slice';
import { Destination } from '../../models/Destination';
import { selectCreateGroupFeatureEnabled } from '../../store/settings.slice';
import { OnboardingStatus } from '../../models';
import { EndpointsList } from '../EndpointsList';
import { CreateGroup } from '../CreateGroup';

const getClassNames = classNamesFunction<ISelectDestinationStyleProps, ISelectDestinationStyles>();

export const SelectDestinationBase: React.FunctionComponent<ISelectDestinationProps> = (props) => {
  const { className, styles, selectedDestination, onSearchDestinationChange, onGroupCreated } = props;
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

  const optionsDestinationType: IComboBoxOption[] = [
    {
      key: 'Group',
      text: 'Group',
      data: {
        description: strings.ManageMembership.labels.groupDescription,
      },
    },
    {
      key: 'Channel',
      text: 'Channel',
      data: {
        description: strings.ManageMembership.labels.channelDescription,
      },
      disabled: true,
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
  const mapDestinationToPersonaProps = (destination: Destination | undefined): IPersonaProps[] => {
    if (!destination) return [];

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
  const selectedDestinationPersona = mapDestinationToPersonaProps(selectedDestination);
  const [createNewGroup, setCreateNewGroup] = useState(false);
  const isCreateGroupEnabled = useSelector(selectCreateGroupFeatureEnabled);

  const [inputValue, setInputValue] = useState('');

  const debouncedSearch = useCallback(
    debounce((input: string) => {
      dispatch(searchDestinations(input));
    }, 50),
    []
  );

  const handleInputChange = (input: string): string => {
    setInputValue(input);
    debouncedSearch(input);
    return input;
  };

  const hasRequiredEndpoints = () => {
    if (!selectedDestinationEndpoints) return false;
    return ['Outlook', 'Yammer', 'SharePoint', 'SecurityGroup'].some((endpoint) =>
      selectedDestinationEndpoints.includes(endpoint)
    );
  };

  const addGroupOwnerLink: string = `https://portal.azure.com/#view/Microsoft_AAD_IAM/GroupDetailsMenuBlade/~/Owners/groupId/${selectedDestination?.id}/menuId/`;

  const appIdNotOwnerWarning =
    onboardingStatus === OnboardingStatus.AppIdNotOwner ? (
      <div className={classNames.ownershipWarning}>
        {strings.ManageMembership.labels.appIdNotOwnerWarning}{' '}
        <a href={addGroupOwnerLink} target="_blank" rel="noopener noreferrer">
          {strings.ManageMembership.labels.clickHere}
        </a>
        .
      </div>
    ) : null;

  const userNotOwnerWarning =
    onboardingStatus === OnboardingStatus.UserNotOwner ? (
      <div className={classNames.ownershipWarning}>{strings.ManageMembership.labels.userNotOwnerWarning}</div>
    ) : null;

  const alreadyOnboardedWarning =
    onboardingStatus === OnboardingStatus.Onboarded ? (
      <div className={classNames.ownershipWarning}>{strings.ManageMembership.labels.alreadyOnboardedWarning}</div>
    ) : null;

  useEffect(() => {}, [dispatch, groupPickerSuggestions]);

  const getPickerSuggestions = async (
    text: string,
    currentGroups: IPersonaProps[] | undefined
  ): Promise<IPersonaProps[]> => {
    return text && groupPickerSuggestions ? groupPickerSuggestions : [];
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
                selectedKey={'Group'}
                onRenderOption={onRenderValueComboBoxOptions}
                styles={{ root: classNames.peoplePicker }}
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
              </div>
            </>
          )}
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
