// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  DetailsListLayoutMode,
  IColumn,
  SelectionMode,
} from '@fluentui/react/lib/DetailsList';
import { useEffect, useState } from 'react';
import { useSelector, useDispatch } from 'react-redux';
import { OdataQueryOptions, fetchJobs } from '../../store/jobs.api';
import {
  selectAllJobs,
  selectGetJobsError,
  setGetJobsError,
  getTotalNumberOfPages,
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
  PrimaryButton
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
import { PageVersion } from '../PageVersion';
import { JobsListFilter } from '../JobsListFilter/JobsListFilter';
import { SyncStatus } from '../../models/Status';
import { useStrings } from '../../localization/hooks';

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

  const strings = useStrings();
  const dispatch = useDispatch<AppDispatch>();
  const jobs = useSelector(selectAllJobs);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState('10');
  const totalNumberOfPages = useSelector(getTotalNumberOfPages);
  const navigate = useNavigate();

  const [filterStatus, setFilterStatus] = useState<string | undefined>(
    undefined
  );
  const [filterActionRequired, setFilterActionRequired] = useState<
    string | undefined
  >(undefined);
  const [filterID, setFilterID] = useState<string | undefined>(undefined);

  const [sortKey, setSortKey] = useState<string | undefined>(undefined);
  const [isSortedDescending, setIsSortedDescending] = useState(false);
  const [isShimmerEnabled, setIsShimmerEnabled] = useState(false);
  const items = jobs;
  const isProduction: boolean = `${process.env.REACT_APP_ENVIRONMENT_ABBREVIATION}` === 'prodv2'; // todo

  const columns = [
    {
      key: 'targetGroupType',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.type,
      fieldName: 'targetGroupType',
      minWidth: 109,
      maxWidth: 109,
      isResizable: true,
      isSorted: sortKey === 'targetGroupType',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'targetGroupName',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.name,
      fieldName: 'targetGroupName',
      minWidth: 439,
      isResizable: true,
      isSorted: sortKey === 'targetGroupName',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'lastSuccessfulRunTime',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.lastRun,
      fieldName: 'lastSuccessfulRunTime',
      minWidth: 114,
      maxWidth: 114,
      isResizable: true,
      isSorted: sortKey === 'lastSuccessfulRunTime',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    },
    {
      key: 'estimatedNextRunTime',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.nextRun,
      fieldName: 'estimatedNextRunTime',
      minWidth: 123,
      maxWidth: 123,
      isResizable: true,
      isSorted: sortKey === 'estimatedNextRunTime',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'enabledOrNot',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.status,
      fieldName: 'enabledOrNot',
      minWidth: 83,
      maxWidth: 83,
      isResizable: true,
      isSorted: sortKey === 'enabledOrNot',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'actionRequired',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.actionRequired,
      fieldName: 'actionRequired',
      minWidth: 200,
      maxWidth: 200,
      isResizable: true,
      isSorted: sortKey === 'actionRequired',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'arrow',
      name: '',
      fieldName: '',
      minWidth: 20,
      maxWidth: 20,
      isResizable: true,
      columnActionsMode: 0,
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
  };

  function onColumnHeaderClick(event?: any, column?: IColumn) {
    if (column) {
      let isSortedDescending = !!column.isSorted && !column.isSortedDescending;
      setIsSortedDescending(isSortedDescending);
      setSortKey(column.key);
      getJobsByPage();
    }
  }

  const onManageMembershipsButtonClick = (): void => {
    navigate('/ManageMembership', { replace: false, state: { item: 1 } });
  };

  const error = useSelector(selectGetJobsError);

  const onDismiss = (): void => {
    dispatch(setGetJobsError());
  };

  const getJobsByPage = (): void => {
    setIsShimmerEnabled(true);
    let orderByString: string | undefined = undefined;
    if (sortKey !== undefined) {
      orderByString = sortKey + (isSortedDescending ? ' desc' : '');
    }

    console.log(filterActionRequired);
    let filterString: string | undefined = undefined;
    if (filterID !== undefined && filterID !== '')
      filterString = 'targetOfficeGroupId eq ' + filterID;
    if (
      filterActionRequired !== undefined &&
      filterActionRequired !== '' &&
      filterActionRequired !== 'All'
    )
      filterString =
        (filterString === undefined ? '' : filterString + ' and ') +
        "status eq '" +
        filterActionRequired +
        "'";
    if (
      filterStatus !== undefined &&
      filterStatus !== '' &&
      filterStatus !== 'All'
    ) {
      if (filterStatus === 'Enabled')
        filterString =
          (filterString === undefined ? '' : filterString + ' and ') +
          "status eq '" +
          SyncStatus.Idle +
          "' or status eq '" +
          SyncStatus.InProgress +
          "'";
      else if (filterStatus === 'Disabled')
        filterString =
          (filterString === undefined ? '' : filterString + ' and ') +
          "not (status eq '" +
          SyncStatus.Idle +
          "' or status eq '" +
          SyncStatus.InProgress +
          "')";
    }

    let odataQueryOptions = new OdataQueryOptions();
    odataQueryOptions.pageSize = parseInt(pageSize) ?? cookies.pageSize;
    odataQueryOptions.itemsToSkip =
      (pageNumber - 1) * odataQueryOptions.pageSize;
    odataQueryOptions.orderBy = orderByString;
    odataQueryOptions.filter = filterString;

    dispatch(fetchJobs(odataQueryOptions));
  };

  useEffect(() => {
    if (
      cookies.pageSize === undefined ||
      cookies.pageSize === 'undefined' ||
      cookies.pageSize === ''
    ) {
      setPageSizeCookie('10');
    } else {
      setPageSize(cookies.pageSize);
    }

    if (!jobs) {
      getJobsByPage();
    } else {
      setIsShimmerEnabled(false);
    }
  }, [dispatch, jobs]);

  const onItemClicked = (
    item?: any,
    index?: number,
    ev?: React.FocusEvent<HTMLElement>
  ): void => {
    if (!item) {
      navigate('/Error', { replace: false, state: { item: 1 } });
      console.log("item was undefined");
    }

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
    <div className={classNames.root}>
      <div className={classNames.jobsListFilter}>
        <JobsListFilter
          setFilterStatus={setFilterStatus}
          setFilterActionRequired={setFilterActionRequired}
          setFilterID={setFilterID}
          getJobsByPage={getJobsByPage}
          filterActionRequired={filterActionRequired}
          filterID={filterID}
          filterStatus={filterStatus}
        />
      </div>
      <div className={classNames.jobsList}>
        <div>
          {error && (
            <MessageBar
              className={classNames.errorMessageBar}
              messageBarType={MessageBarType.error}
              isMultiline={false}
              onDismiss={onDismiss}
              dismissButtonAriaLabel={strings.JobsList.MessageBar.dismissButtonAriaLabel}
            >
              {error}
            </MessageBar>
          )}
          <div className={classNames.titleContainer}>
            <div className={classNames.title}>
              <Text variant="xLarge">{strings.JobsList.listOfMemberships}</Text>
            </div>
            {!isProduction ?
              <PrimaryButton onClick={onManageMembershipsButtonClick}>{strings.ManageMembership.manageMembershipButton}</PrimaryButton>
              : null
            }
          </div>
          <div className={classNames.tabContent}>
            <ShimmeredDetailsList
              setKey="set"
              onColumnHeaderClick={onColumnHeaderClick}
              items={sortedItems || []}
              columns={columns}
              enableShimmer={!jobs || isShimmerEnabled}
              layoutMode={DetailsListLayoutMode.justified}
              selectionMode={SelectionMode.none}
              ariaLabelForShimmer="Content is being fetched"
              ariaLabelForGrid="Item details"
              selectionPreservedOnEmptyClick={true}
              ariaLabelForSelectionColumn={strings.JobsList.ShimmeredDetailsList.toggleSelection}
              ariaLabelForSelectAllCheckbox={strings.JobsList.ShimmeredDetailsList.toggleAllSelection}
              checkButtonAriaLabel={strings.JobsList.ShimmeredDetailsList.selectRow}
              onActiveItemChanged={onItemClicked}
              onRenderItemColumn={_renderItemColumn}
            />

            {jobs?.length === 0 && (
              <div className={classNames.noMembershipsFoundText}>
                <Text variant="medium">{strings.JobsList.NoResults}</Text>
              </div>
            )}
            <div className={classNames.columnToEnd}></div>
          </div>
        </div>
      </div>
      <div className={classNames.footer}>
        <PageVersion />
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
    </div>
  );
};
