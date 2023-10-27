// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  classNamesFunction,
  IProcessedStyleSet,
  IconButton,
  Stack,
  TextField,
  IDropdownOption,
  Dropdown,
  IStackTokens,
  DefaultButton,
  TooltipHost,
  DirectionalHint,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import {
  IJobsListFilterProps,
  IJobsListFilterStyleProps,
  IJobsListFilterStyles,
} from './JobsListFilter.types';
import { SyncStatus } from '../../models/Status';
import { useState } from 'react';
import { useStrings } from '../../localization';

const getClassNames = classNamesFunction<
  IJobsListFilterStyleProps,
  IJobsListFilterStyles
>();

export const JobsListFilterBase: React.FunctionComponent<
  IJobsListFilterProps
> = (props: IJobsListFilterProps) => {
  const {
    className,
    styles,
    getJobsByPage,
    setFilterStatus,
    setFilterActionRequired,
    setFilterID,
  } = props;

  const classNames: IProcessedStyleSet<IJobsListFilterStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  const strings = useStrings();
  const [ID, setID] = useState<string>('');
  const [statusSelectedItem, setStatusSelectedItem] =
    useState<IDropdownOption>();
  const [actionRequiredSelectedItem, setActionRequiredSelectedItem] =
    useState<IDropdownOption>();
  const [idValidationErrorMessage, setIdValidationErrorMessage] =
    useState<string>();

  const itemAlignmentsStackTokens: IStackTokens = {
    childrenGap: 18,
  };

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
  ];

  const onChangeID = (
    event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>,
    newValue?: string
  ): void => {
    const inputGuid = newValue || '';

    if (inputGuid !== '' && !isGuidValid(inputGuid)) {
      setIdValidationErrorMessage(strings.JobsList.JobsListFilter.filters.ID.validationErrorMessage);
    } else {
      setIdValidationErrorMessage(undefined);
    }

    setID(inputGuid);
    setFilterID(inputGuid);
  };
  const onChangeStatus = (
    event: React.FormEvent<HTMLDivElement>,
    item?: IDropdownOption
  ): void => {
    setStatusSelectedItem(item);
    setFilterStatus(item?.key.toString() || '');
  };
  const onChangeActionRequired = (
    event: React.FormEvent<HTMLDivElement>,
    item?: IDropdownOption
  ): void => {
    setActionRequiredSelectedItem(item);
    setFilterActionRequired(item?.key.toString() || '');
  };

  const isGuidValid = (guid: string): boolean => {
    const guidRegex = new RegExp(
      /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
    );
    return guidRegex.test(guid);
  };

  const getFilteredJobs = () => {
    if (idValidationErrorMessage !== undefined) {
      return;
    }
    getJobsByPage();
  };

  const clearFilters = () => {
    setFilterID('');
    setFilterStatus('');
    setFilterActionRequired('');
    setID('');
    setIdValidationErrorMessage(undefined);
    setActionRequiredSelectedItem(actionRequiredDropdownOptions[0]);
    setStatusSelectedItem(statusDropdownOptions[0]);
  };

  return (
    <div className={classNames.container}>
      <div>
        <Stack horizontal tokens={itemAlignmentsStackTokens}>
          <Stack.Item align="start">
            <TextField
              label={strings.JobsList.JobsListFilter.filters.ID.label}
              value={ID}
              onChange={onChangeID}
              placeholder={strings.JobsList.JobsListFilter.filters.ID.placeholder}
              errorMessage={idValidationErrorMessage}
              styles={{
                fieldGroup: classNames.textFieldFieldGroup,
              }}
            />
          </Stack.Item>

          <Stack.Item align="start">
            <Dropdown
              label={strings.JobsList.JobsListFilter.filters.status.label}
              selectedKey={
                statusSelectedItem ? statusSelectedItem.key : undefined
              }
              onChange={onChangeStatus}
              defaultSelectedKey="All"
              options={statusDropdownOptions}
              styles={{
                title: classNames.dropdownTitle
              }}
            />
          </Stack.Item>

          <Stack.Item align="start">
            <Dropdown
              label={strings.JobsList.JobsListFilter.filters.actionRequired.label}
              selectedKey={
                actionRequiredSelectedItem
                  ? actionRequiredSelectedItem.key
                  : undefined
              }
              onChange={onChangeActionRequired}
              defaultSelectedKey="All"
              options={actionRequiredDropdownOptions}
              styles={{
                title: classNames.dropdownTitle
              }}
            />
          </Stack.Item>

          <Stack.Item
            align="start"
            className={classNames.filterButtonStackItem}
          >
            <DefaultButton
              text={strings.JobsList.JobsListFilter.filterButtonText}
              onClick={getFilteredJobs}
              className={classNames.filterButton}
            />
          </Stack.Item>

          <Stack.Item
            align="start"
            className={classNames.filterButtonStackItem}
          >
            <TooltipHost
              content={strings.JobsList.JobsListFilter.clearButtonTooltip}
              styles={{
                root: classNames.clearFilterTooltip,
              }}
              directionalHint={DirectionalHint.topRightEdge}
              calloutProps={{
                beakWidth: 8,
              }}
            >
              <IconButton
                iconProps={{
                  iconName: 'ClearFilter',
                  styles: { root: classNames.clearFilterIconButton },
                }}
                onClick={clearFilters}
              />
            </TooltipHost>
          </Stack.Item>
        </Stack>
      </div>
    </div>
  );
};
