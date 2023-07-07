// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  DetailsListLayoutMode,
  IColumn,
  SelectionMode,
} from '@fluentui/react/lib/DetailsList';
import { useTranslation } from 'react-i18next';
import '../../i18n/config';
import { useEffect, useState } from 'react';
import { useSelector, useDispatch } from 'react-redux';
import { fetchJobs } from '../../store/jobs.api';
import {
  selectAllJobs,
  selectGetJobsError,
  setGetJobsError,
} from '../../store/jobs.slice';
import { AppDispatch } from '../../store';

import { useNavigate } from 'react-router-dom';
import {
  classNamesFunction,
  IProcessedStyleSet,
  MessageBar,
  MessageBarType,
  IconButton,
  IIconProps,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { ShimmeredDetailsList } from '@fluentui/react/lib/ShimmeredDetailsList';
import {
  IJobsListProps,
  IJobsListStyleProps,
  IJobsListStyles,
} from './JobsList.types';
import {
  ReportHackedIcon,
  ChevronRightMedIcon,
} from '@fluentui/react-icons-mdl2';

const getClassNames = classNamesFunction<
  IJobsListStyleProps,
  IJobsListStyles
>();

export const JobsListBase: React.FunctionComponent<IJobsListProps> = (
  props: IJobsListProps
) => {
  const { className, styles } = props;

  const classNames: IProcessedStyleSet<IJobsListStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  const { t } = useTranslation();
  const dispatch = useDispatch<AppDispatch>();
  const jobs = useSelector(selectAllJobs);
  const navigate = useNavigate();

  const [sortKey, setSortKey] = useState<string | undefined>(undefined);
  const [isSortedDescending, setIsSortedDescending] = useState(false);
  const items = jobs;

  const columns = [
    {
      key: 'targetGroupType',
      name: t('JobsList.ShimmeredDetailsList.columnNames.type'),
      fieldName: 'targetGroupType',
      minWidth: 100,
      maxWidth: 100,
      isResizable: false,
      isSorted: sortKey === 'targetGroupType',
      isSortedDescending,
    },
    {
      key: 'targetGroupName',
      name: t('JobsList.ShimmeredDetailsList.columnNames.name'),
      fieldName: 'targetGroupName',
      minWidth: 100,
      maxWidth: 100,
      isResizable: false,
      isSorted: sortKey === 'targetGroupName',
      isSortedDescending,
    },
    {
      key: 'lastSuccessfulRunTime',
      name: t('JobsList.ShimmeredDetailsList.columnNames.lastRun'),
      fieldName: 'lastSuccessfulRunTime',
      minWidth: 100,
      maxWidth: 100,
      isResizable: false,
      isSorted: sortKey === 'lastSuccessfulRunTime',
      isSortedDescending,
    },
    {
      key: 'estimatedNextRunTime',
      name: t('JobsList.ShimmeredDetailsList.columnNames.nextRun'),
      fieldName: 'estimatedNextRunTime',
      minWidth: 100,
      maxWidth: 100,
      isResizable: false,
      isSorted: sortKey === 'estimatedNextRunTime',
      isSortedDescending,
    },
    {
      key: 'enabledOrNot',
      name: t('JobsList.ShimmeredDetailsList.columnNames.status'),
      fieldName: 'enabledOrNot',
      minWidth: 75,
      maxWidth: 75,
      isResizable: false,
      isSorted: sortKey === 'enabledOrNot',
      isSortedDescending,
    },
    {
      key: 'actionRequired',
      name: t('JobsList.ShimmeredDetailsList.columnNames.actionRequired'),
      fieldName: 'actionRequired',
      minWidth: 200,
      maxWidth: 200,
      isResizable: false,
      isSorted: sortKey === 'actionRequired',
      isSortedDescending,
    },
    {
      key: 'arrow',
      name: '',
      fieldName: '',
      minWidth: 200,
      maxWidth: 200,
      isResizable: false,
    },
  ];

  const sortedItems = [...(items ? items : [])].sort((a, b) => {
    if (
      sortKey === 'enabledOrNot' ||
      sortKey === 'lastSuccessfulRunTime' ||
      sortKey === 'estimatedNextRunTime' ||
      sortKey === 'targetGroupName' ||
      sortKey === 'targetGroupType' ||
      sortKey === 'actionRequired'
    ) {
      return isSortedDescending
        ? (b[sortKey] || '').localeCompare(a[sortKey] || '')
        : (a[sortKey] || '').localeCompare(b[sortKey] || '');
    }
    return 0;
  });

  function onColumnHeaderClick(event?: any, column?: IColumn) {
    if (column) {
      setIsSortedDescending(!!column.isSorted && !column.isSortedDescending);
      setSortKey(column.key);
    }
  }

  const error = useSelector(selectGetJobsError);

  const onDismiss = (): void => {
    dispatch(setGetJobsError());
  };

  useEffect(() => {
    if (!jobs) {
      dispatch(fetchJobs());
    }
  }, [dispatch, jobs]);

  const onItemClicked = (
    item?: any,
    index?: number,
    ev?: React.FocusEvent<HTMLElement>
  ): void => {
    navigate('/JobDetailsPage', { replace: false, state: { item: item } });
  };

  const onRefreshClicked = (
    item?: any,
    index?: number,
    ev?: React.FocusEvent<HTMLElement>
  ): void => {
    dispatch(fetchJobs());
  };

  const refreshIcon: IIconProps = { iconName: 'Refresh' };

  const _renderItemColumn = (
    item?: any,
    index?: number,
    column?: IColumn
  ): JSX.Element => {
    const fieldContent = item[column?.fieldName as keyof any] as string;

    switch (column?.key) {
      case 'lastSuccessfulRunTime':
      case 'estimatedNextRunTime':
        const spaceIndex = fieldContent.indexOf(' ');
        const isEmpty = fieldContent === '';
        const lastOrNextRunDate = isEmpty
          ? '-'
          : fieldContent.substring(0, spaceIndex);
        const hoursAgoOrHoursLeft = isEmpty
          ? ''
          : fieldContent.substring(spaceIndex + 1);

        return (
          <div>
            <div>{lastOrNextRunDate}</div>
            <div>{hoursAgoOrHoursLeft}</div>
          </div>
        );

      case 'enabledOrNot':
        return (
          <div>
            {fieldContent === 'Disabled' ? (
              <div className={classNames.disabled}> {fieldContent}</div>
            ) : (
              <div className={classNames.enabled}> {fieldContent}</div>
            )}
          </div>
        );

      case 'actionRequired':
        return (
          <div>
            {fieldContent ? (
              <div className={classNames.actionRequired}>
                {' '}
                <ReportHackedIcon /> {fieldContent}
              </div>
            ) : (
              <div className={classNames.actionRequired}> {fieldContent}</div>
            )}
          </div>
        );

      case 'arrow':
        return fieldContent ? (
          <IconButton
            iconProps={refreshIcon}
            title="Refresh"
            ariaLabel="Refresh"
            onClick={onRefreshClicked}
          />
        ) : (
          <ChevronRightMedIcon />
        );

      default:
        return <span>{fieldContent}</span>;
    }
  };

  return (
    <div className={classNames.root}>
      {error && (
        <MessageBar
          messageBarType={MessageBarType.error}
          isMultiline={false}
          onDismiss={onDismiss}
          dismissButtonAriaLabel={
            t('JobsList.MessageBar.dismissButtonAriaLabel') as
              | string
              | undefined
          }
        >
          {error}
        </MessageBar>
      )}

      <div className={classNames.tabContent}>
        <ShimmeredDetailsList
          setKey="set"
          onColumnHeaderClick={onColumnHeaderClick}
          items={sortedItems || []}
          columns={columns}
          enableShimmer={!jobs || jobs.length === 0}
          layoutMode={DetailsListLayoutMode.justified}
          selectionMode={SelectionMode.none}
          ariaLabelForShimmer="Content is being fetched"
          ariaLabelForGrid="Item details"
          selectionPreservedOnEmptyClick={true}
          ariaLabelForSelectionColumn={
            t('JobsList.ShimmeredDetailsList.toggleSelection') as
              | string
              | undefined
          }
          ariaLabelForSelectAllCheckbox={
            t('JobsList.ShimmeredDetailsList.toggleAllSelection') as
              | string
              | undefined
          }
          checkButtonAriaLabel={
            t('JobsList.ShimmeredDetailsList.selectRow') as string | undefined
          }
          onActiveItemChanged={onItemClicked}
          onRenderItemColumn={_renderItemColumn}
        />
      </div>
    </div>
  );
};
