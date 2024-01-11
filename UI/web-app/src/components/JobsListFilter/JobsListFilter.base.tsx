// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  classNamesFunction,
  IProcessedStyleSet,
  Stack,
  TextField,
  Text,
  IDropdownOption,
  Dropdown,
  DefaultButton,
  DirectionalHint,
  NormalPeoplePicker,
  Label
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { IJobsListFilterProps, IJobsListFilterStyleProps, IJobsListFilterStyles } from './JobsListFilter.types';
import { SyncStatus } from '../../models/Status';
import { useEffect, useState } from 'react';
import { useStrings } from '../../store/hooks';
import { IPersonaProps } from '@fluentui/react/lib/Persona';
import { useDispatch, useSelector } from 'react-redux';
import { selectIsAdmin } from '../../store/roles.slice';
import { AppDispatch } from '../../store';
import { selectJobOwnerFilterSuggestions } from '../../store/jobs.slice';
import { getJobOwnerFilterSuggestions } from '../../store/jobs.api';
import { 
  setFilterActionRequired, 
  setFilterStatus,
  setFilterDestinationId,
  setFilterDestinationType,
  setFilterDestinationName,
  setFilterDestinationOwner
} from '../../store/pagingBar.slice';

const getClassNames = classNamesFunction<IJobsListFilterStyleProps, IJobsListFilterStyles>();

export const JobsListFilterBase: React.FunctionComponent<IJobsListFilterProps> = (props: IJobsListFilterProps) => {
  const {
    className,
    styles,
    getJobsByPage
  } = props;

  const classNames: IProcessedStyleSet<IJobsListFilterStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  const strings = useStrings();

  const statusDropdownOptions = [
    {
      key: 'All',
      text: strings.JobsList.JobsListFilter.filters.status.options.all,
    },
    {
      key: 'Enabled',
      text: strings.JobsList.JobsListFilter.filters.status.options.enabled,
    },
    {
      key: 'Disabled',
      text: strings.JobsList.JobsListFilter.filters.status.options.disabled,
    },
  ];

  const typeDropdownOptions = [
    {
      key: 'All',
      text: strings.JobsList.JobsListFilter.filters.destinationType.options.all,
    },
    {
      key: 'TeamsChannelMembership',
      text: strings.JobsList.JobsListFilter.filters.destinationType.options.channel,
    },
    {
      key: 'GroupMembership',
      text: strings.JobsList.JobsListFilter.filters.destinationType.options.group,
    },
  ];

  const actionRequiredDropdownOptions = [
    {
      key: 'All',
      text: strings.JobsList.JobsListFilter.filters.actionRequired.options.all,
    },
    {
      key: SyncStatus.ThresholdExceeded,
      text: strings.JobsList.JobsListFilter.filters.actionRequired.options.thresholdExceeded,
    },
    {
      key: SyncStatus.CustomerPaused,
      text: strings.JobsList.JobsListFilter.filters.actionRequired.options.customerPaused,
    },
    {
      key: SyncStatus.MembershipDataNotFound,
      text: strings.JobsList.JobsListFilter.filters.actionRequired.options.membershipDataNotFound,
    },
    {
      key: SyncStatus.DestinationGroupNotFound,
      text: strings.JobsList.JobsListFilter.filters.actionRequired.options.destinationGroupNotFound,
    },
    {
      key: SyncStatus.NotOwnerOfDestinationGroup,
      text: strings.JobsList.JobsListFilter.filters.actionRequired.options.notOwnerOfDestinationGroup,
    },
    {
      key: SyncStatus.SecurityGroupNotFound,
      text: strings.JobsList.JobsListFilter.filters.actionRequired.options.securityGroupNotFound,
    },
    {
      key: SyncStatus.PendingReview,
      text: strings.JobsList.JobsListFilter.filters.actionRequired.options.pendingReview,
    }
  ];

  const [destinationId, setDestinationId] = useState<string>('');
  const [statusSelectedItem, setStatusSelectedItem] = useState<IDropdownOption>(statusDropdownOptions[0]);
  const [actionRequiredSelectedItem, setActionRequiredSelectedItem] = useState<IDropdownOption>(actionRequiredDropdownOptions[0]);
  const [destinationTypeSelectedItem, setDestinationTypeSelectedItem] = useState<IDropdownOption>(typeDropdownOptions[0]);
  const [destinationName, setDestinationName] = useState<string>();
  const [idValidationErrorMessage, setIdValidationErrorMessage] = useState<string>();
  const [selectedOwners, setSelectedOwners] = useState<IPersonaProps[]>([]);
  const isAdmin = useSelector(selectIsAdmin);
  const ownerPickerSuggestions = useSelector(selectJobOwnerFilterSuggestions);
  const dispatch = useDispatch<AppDispatch>();

  useEffect(() => {
    
  }, [dispatch, ownerPickerSuggestions]);

  const handleIdChanged = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string): void => {
    const inputGuid = newValue || '';

    if (inputGuid !== '' && !isGuidValid(inputGuid)) {
      setIdValidationErrorMessage(strings.JobsList.JobsListFilter.filters.ID.validationErrorMessage);
    } else {
      setIdValidationErrorMessage(undefined);
    }

    setDestinationId(inputGuid);
    dispatch(setFilterDestinationId(inputGuid));
  };

  const handleNameChanged = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string): void => {
    setDestinationName(newValue);
    dispatch(setFilterDestinationName(newValue as string));
  };

  const handleStatusChanged = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption): void => {
    setStatusSelectedItem(item as IDropdownOption);
    dispatch(setFilterStatus(item?.key.toString() || ''));
  };

  const handleTypeChanged = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption): void => {
    setDestinationTypeSelectedItem(item as IDropdownOption);
    dispatch(setFilterDestinationType(item?.key.toString() || ''));
  };

  const handleActionRequiredChanged = (event: React.FormEvent<HTMLDivElement>, item?: IDropdownOption): void => {
    setActionRequiredSelectedItem(item as IDropdownOption);
    dispatch(setFilterActionRequired(item?.key.toString() || ''));
  };

  const handleOwnersChanged = (items?: IPersonaProps[] | undefined) => {
    if (items !== undefined && items.length > 0) {
      setSelectedOwners(items);
      dispatch(setFilterDestinationOwner(items[0].id as string));
    }
    else
    {
      setSelectedOwners([]);
      dispatch(setFilterDestinationOwner(''));
    }    
  };

  const handleOwnersInputChanged = (input: string): string => {
    if (input.trim()) {
      dispatch(getJobOwnerFilterSuggestions({displayName: input, alias: input}))
    }
    return input;
  }
  
  const isGuidValid = (guid: string): boolean => {
    const guidRegex = new RegExp(/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/);
    return guidRegex.test(guid);
  };

  const getFilteredJobs = () => {
    if (idValidationErrorMessage !== undefined) {
      return;
    }
    getJobsByPage();
  };

  const clearFilters = () => {
    dispatch(setFilterDestinationId(''));
    dispatch(setFilterDestinationType(''));
    dispatch(setFilterDestinationName(''));
    dispatch(setFilterDestinationOwner(''));
    dispatch(setFilterStatus(''));
    dispatch(setFilterActionRequired(''));

    setDestinationId('');
    setDestinationName('');
    setSelectedOwners([]);
    setDestinationTypeSelectedItem(typeDropdownOptions[0]);
    setIdValidationErrorMessage(undefined);
    setActionRequiredSelectedItem(actionRequiredDropdownOptions[0]);
    setStatusSelectedItem(statusDropdownOptions[0]);
  };

  const getPickerSuggestions = async (
    filterText: string,
    currentPersonas: IPersonaProps[] | undefined
  ): Promise<IPersonaProps[]> => {
    return filterText && ownerPickerSuggestions ? ownerPickerSuggestions : [];
  };

  return (
    <div className={classNames.container}>

      <Stack>

        {/*Filter Header*/}
        <Stack.Item className={classNames.filterHeaderContainer}>

          <Stack horizontal>

            <Stack.Item align="start">
              <Text className={classNames.filterTitleText}>Filters</Text>
            </Stack.Item>

            <Stack.Item grow>
              <div></div>
            </Stack.Item>

            <Stack.Item align="end">
              <DefaultButton
                iconProps={{
                  iconName: 'ClearFilter',
                  styles: { root: classNames.filterButtonIcon },
                }}
                text={'Clear'}
                onClick={clearFilters}
                className={classNames.clearFilterButton}
              />
            </Stack.Item>

          </Stack>

        </Stack.Item>

        {/*Filter Inputs*/}
        <Stack.Item align="start" className={classNames.filterInputsContainer}>

          <Stack
            horizontal
            tokens={{
              childrenGap: 18,
              maxWidth: 1400,
            }}
            horizontalAlign="space-between"
            className={classNames.filterInputsStack}
          >

            <Stack.Item align="start">
              <Dropdown
                label={strings.JobsList.JobsListFilter.filters.destinationType.label}
                selectedKey={destinationTypeSelectedItem ? destinationTypeSelectedItem.key : undefined}
                onChange={handleTypeChanged}
                options={typeDropdownOptions}
                styles={{
                  title: classNames.dropdownTitle,
                }}
              />
            </Stack.Item>

            <Stack.Item align="start">
              <TextField
                label={strings.JobsList.JobsListFilter.filters.destinationName.label}
                value={destinationName}
                onChange={handleNameChanged}
                placeholder={strings.JobsList.JobsListFilter.filters.ID.placeholder}
                styles={{
                  fieldGroup: classNames.textFieldFieldGroup,
                }}
              />
            </Stack.Item>

            <Stack.Item align="start">
              <Dropdown
                label={strings.JobsList.JobsListFilter.filters.status.label}
                selectedKey={statusSelectedItem ? statusSelectedItem.key : undefined}
                onChange={handleStatusChanged}
                options={statusDropdownOptions}
                styles={{
                  title: classNames.dropdownTitle,
                }}
              />
            </Stack.Item>

            <Stack.Item align="start">
              <Dropdown
                label={strings.JobsList.JobsListFilter.filters.actionRequired.label}
                selectedKey={actionRequiredSelectedItem ? actionRequiredSelectedItem.key : undefined}
                onChange={handleActionRequiredChanged}
                dropdownWidth={'auto'}
                options={actionRequiredDropdownOptions}
                styles={{
                  title: classNames.dropdownTitle,
                }}
              />
            </Stack.Item>

            <Stack.Item align="start">
              <TextField
                label={strings.JobsList.JobsListFilter.filters.ID.label}
                value={destinationId}
                onChange={handleIdChanged}
                placeholder={strings.JobsList.JobsListFilter.filters.ID.placeholder}
                errorMessage={idValidationErrorMessage}
                styles={{
                  fieldGroup: classNames.textFieldFieldGroupGuid,
                }}
              />
            </Stack.Item>

            {isAdmin ? (
              <Stack.Item align="start">
                <Label>{strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.label}</Label>
                <NormalPeoplePicker
                  onResolveSuggestions={getPickerSuggestions}
                  pickerSuggestionsProps={{
                    suggestionsHeaderText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.suggestionsHeaderText,
                    noResultsFoundText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.noResultsFoundText,
                    loadingText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.loadingText,
                  }}
                  key={'normal'}
                  selectionAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.selectionAriaLabel}
                  removeButtonAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.removeButtonAriaLabel}
                  resolveDelay={300}
                  itemLimit={1}
                  selectedItems={selectedOwners}
                  onInputChange={handleOwnersInputChanged}
                  onChange={handleOwnersChanged}
                  styles={
                    {
                      text: classNames.peoplePicker,
                    }
                  } 
                  pickerCalloutProps={
                    {
                      directionalHint: DirectionalHint.bottomCenter,
                    }
                  }
                />
              </Stack.Item>
            ) : (
              <Stack.Item align="start"> <div className={classNames.emptyStackItem}></div> </Stack.Item>
            )}

          </Stack>

        </Stack.Item>

      </Stack>

    </div>
  );
};
                                           