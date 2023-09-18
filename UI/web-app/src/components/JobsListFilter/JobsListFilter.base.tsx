// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useTranslation } from 'react-i18next';
import '../../i18n/config';
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
import {
  clearFilterTooltipStyles,
  dropdownStyles,
  textFieldStyles,
} from './JobsListFilter.styles';
import { useState } from 'react';

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

  const { t } = useTranslation();
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
      text: t('JobsList.JobsListFilter.filters.status.options.all'),
    },
    {
      key: 'Enabled',
      text: t('JobsList.JobsListFilter.filters.status.options.enabled'),
    },
    {
      key: 'Disabled',
      text: t('JobsList.JobsListFilter.filters.status.options.disabled'),
    },
  ];

  const actionRequiredDropdownOptions = [
    {
      key: 'All',
      text: t('JobsList.JobsListFilter.filters.actionRequired.options.all'),
    },
    {
      key: SyncStatus.ThresholdExceeded,
      text: t(
        'JobsList.JobsListFilter.filters.actionRequired.options.thresholdExceeded'
      ),
    },
    {
      key: SyncStatus.CustomerPaused,
      text: t(
        'JobsList.JobsListFilter.filters.actionRequired.options.customerPaused'
      ),
    },
    {
      key: SyncStatus.MembershipDataNotFound,
      text: t(
        'JobsList.JobsListFilter.filters.actionRequired.options.membershipDataNotFound'
      ),
    },
    {
      key: SyncStatus.DestinationGroupNotFound,
      text: t(
        'JobsList.JobsListFilter.filters.actionRequired.options.destinationGroupNotFound'
      ),
    },
    {
      key: SyncStatus.NotOwnerOfDestinationGroup,
      text: t(
        'JobsList.JobsListFilter.filters.actionRequired.options.notOwnerOfDestinationGroup'
      ),
    },
    {
      key: SyncStatus.SecurityGroupNotFound,
      text: t(
        'JobsList.JobsListFilter.filters.actionRequired.options.securityGroupNotFound'
      ),
    },
  ];

  const onChangeID = (
    event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>,
    newValue?: string
  ): void => {
    const inputGuid = newValue || '';

    if (inputGuid !== '' && !isGuidValid(inputGuid)) {
      setIdValidationErrorMessage(
        t('JobsList.JobsListFilter.filters.ID.validationErrorMessage') as string
      );
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
              label={t('JobsList.JobsListFilter.filters.ID.label') as string}
              value={ID}
              onChange={onChangeID}
              placeholder={
                t('JobsList.JobsListFilter.filters.ID.placeholder') as string
              }
              errorMessage={idValidationErrorMessage}
              styles={textFieldStyles}
            />
          </Stack.Item>

          <Stack.Item align="start">
            <Dropdown
              label={
                t('JobsList.JobsListFilter.filters.status.label') as string
              }
              selectedKey={
                statusSelectedItem ? statusSelectedItem.key : undefined
              }
              onChange={onChangeStatus}
              defaultSelectedKey="All"
              options={statusDropdownOptions}
              styles={dropdownStyles}
            />
          </Stack.Item>

          <Stack.Item align="start">
            <Dropdown
              label={
                t(
                  'JobsList.JobsListFilter.filters.actionRequired.label'
                ) as string
              }
              selectedKey={
                actionRequiredSelectedItem
                  ? actionRequiredSelectedItem.key
                  : undefined
              }
              onChange={onChangeActionRequired}
              defaultSelectedKey="All"
              options={actionRequiredDropdownOptions}
              styles={dropdownStyles}
            />
          </Stack.Item>

          <Stack.Item
            align="start"
            className={classNames.filterButtonStackItem}
          >
            <DefaultButton
              text={t('JobsList.JobsListFilter.filterButtonText') as string}
              onClick={getFilteredJobs}
              className={classNames.filterButton}
            />
          </Stack.Item>

          <Stack.Item
            align="start"
            className={classNames.filterButtonStackItem}
          >
            <TooltipHost
              content={
                t('JobsList.JobsListFilter.clearButtonTooltip') as string
              }
              styles={clearFilterTooltipStyles}
              directionalHint={DirectionalHint.topRightEdge}
              calloutProps={{
                beakWidth: 8,
              }}
            >
              <IconButton
                iconProps={{
                  iconName: 'ClearFilter',
                  styles: { root: { fontSize: 20 } },
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
