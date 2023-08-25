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
import { OdataQueryOptions, fetchJobs } from '../../store/jobs.api';
import {
  selectAllJobs,
  selectGetJobsError,
  setGetJobsError,
  getTotalNumberOfPages
} from '../../store/jobs.slice';
import { AppDispatch } from '../../store';

import { useNavigate } from 'react-router-dom';
import {
  classNamesFunction,
  IProcessedStyleSet,
  MessageBar,
  MessageBarType,
  IconButton,
  IIconProps
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { Text } from '@fluentui/react/lib/Text';
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
import { useCookies } from 'react-cookie';
import { PagingBar } from '../PagingBar';

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
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState("10");
  const totalNumberOfPages = useSelector(getTotalNumberOfPages);
  const navigate = useNavigate();

  const [sortKey, setSortKey] = useState<string | undefined>(undefined);
  const [isSortedDescending, setIsSortedDescending] = useState(false);
  const items = jobs;

  const columns = [
    {
      key: 'targetGroupType',
      name: t('JobsList.ShimmeredDetailsList.columnNames.type'),
      fieldName: 'targetGroupType',
      minWidth: 109,
      isResizable: true,
      isSorted: sortKey === 'targetGroupType',
      isSortedDescending,
      columnActionsMode: 0
    },
    {
      key: 'targetGroupName',
      name: t('JobsList.ShimmeredDetailsList.columnNames.name'),
      fieldName: 'targetGroupName',
      minWidth: 439,
      isResizable: true,
      isSorted: sortKey === 'targetGroupName',
      isSortedDescending,
      columnActionsMode: 0
    },
    {
      key: 'lastSuccessfulRunTime',
      name: t('JobsList.ShimmeredDetailsList.columnNames.lastRun'),
      fieldName: 'lastSuccessfulRunTime',
      minWidth: 114,
      isResizable: true,
      isSorted: sortKey === 'lastSuccessfulRunTime',
      isSortedDescending,
      showSortIconWhenUnsorted: true
    },
    {
      key: 'estimatedNextRunTime',
      name: t('JobsList.ShimmeredDetailsList.columnNames.nextRun'),
      fieldName: 'estimatedNextRunTime',
      minWidth: 123,
      isResizable: true,
      isSorted: sortKey === 'estimatedNextRunTime',
      isSortedDescending,
      columnActionsMode: 0
    },
    {
      key: 'enabledOrNot',
      name: t('JobsList.ShimmeredDetailsList.columnNames.status'),
      fieldName: 'enabledOrNot',
      minWidth: 83,
      isResizable: true,
      isSorted: sortKey === 'enabledOrNot',
      isSortedDescending,
      columnActionsMode: 0
    },
    {
      key: 'actionRequired',
      name: t('JobsList.ShimmeredDetailsList.columnNames.actionRequired'),
      fieldName: 'actionRequired',
      minWidth: 140,
      isResizable: true,
      isSorted: sortKey === 'actionRequired',
      isSortedDescending,
      columnActionsMode: 0
    },
    {
      key: 'arrow',
      name: '',
      fieldName: '',
      minWidth: 200,
      isResizable: true,
      columnActionsMode: 0
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

  const [cookies, setCookie] = useCookies(['pageSize']);
  const setPageSizeCookie = (pageSize: string): void => {
    setCookie('pageSize', pageSize, { path: '/' });
  }

  function onColumnHeaderClick(event?: any, column?: IColumn) {
    if (column) {
      let isSortedDescending = !!column.isSorted && !column.isSortedDescending;
      setIsSortedDescending(isSortedDescending);
      setSortKey(column.key);
      getJobsByPage(parseInt(pageSize), pageNumber, column.key + (isSortedDescending ? ' desc' : ''));
    }
  }

  const error = useSelector(selectGetJobsError);

  const onDismiss = (): void => {
    dispatch(setGetJobsError());
  };

  const getJobsByPage = (currentPageSize?: number, currentPageNumber?: number, orderBy?: string): void => {

    let orderByString: string | undefined = undefined;
    if (orderBy !== undefined)
      orderByString = orderBy;
    else if (sortKey !== undefined) {
      orderByString = sortKey + (isSortedDescending ? ' desc' : '');
    }

    let odataQueryOptions = new OdataQueryOptions();
    odataQueryOptions.pageSize = currentPageSize ?? parseInt(pageSize);
    odataQueryOptions.itemsToSkip = ((currentPageNumber ?? pageNumber) - 1) * odataQueryOptions.pageSize;
    odataQueryOptions.orderBy = orderByString
    dispatch(fetchJobs(odataQueryOptions));
  }

  useEffect(() => {

    if (cookies.pageSize === undefined || cookies.pageSize === 'undefined' || cookies.pageSize === '') {
      setPageSizeCookie('10');
    }
    else {
      setPageSize(cookies.pageSize);
    }

    if (!jobs) {
      getJobsByPage(cookies.pageSize ?? pageSize ?? 10);
    }
  }, [dispatch, jobs]);

  const onItemClicked = (
    item?: any,
    index?: number,
    ev?: React.FocusEvent<HTMLElement>
  ): void => {
    navigate('/JobDetails', { replace: false, state: { item: item } });
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
    <div>
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
        <div className={classNames.title}>
          <Text variant='xLarge'>{t('JobsList.listOfMemberships')}</Text>
        </div>
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
      <PagingBar
        pageSize={pageSize}
        pageNumber={pageNumber}
        totalNumberOfPages={totalNumberOfPages ?? 1}
        getJobsByPage={getJobsByPage}
        setPageSize={setPageSize}
        setPageNumber={setPageNumber}
        setPageSizeCookie={setPageSizeCookie}
      />
    </div>
  );
};
