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
  IIconProps,
  IButtonProps,
  TextField,
  Dropdown,
  IDropdownOption,
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
import { useCookies } from 'react-cookie';

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
      minWidth: 100,
      maxWidth: 100,
      isResizable: false,
      isSorted: sortKey === 'targetGroupType',
      isSortedDescending,
      columnActionsMode: 0
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
      columnActionsMode: 0
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
      columnActionsMode: 0
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
      columnActionsMode: 0
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
      columnActionsMode: 0
    },
    {
      key: 'arrow',
      name: '',
      fieldName: '',
      minWidth: 200,
      maxWidth: 200,
      isResizable: false,
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

        <div className={classNames.tabContent}>
          <ShimmeredDetailsList
            setKey="set"
            onColumnHeaderClick={onColumnHeaderClick}
            items={items || []}
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

export interface IPagingBarProps extends React.AllHTMLAttributes<HTMLElement> {
  pageSize: string;
  pageNumber: number;
  totalNumberOfPages: number;
  setPageSize: (pageSize: string) => void;
  setPageNumber: (pageNumber: number) => void;
  setPageSizeCookie: (pageSize: string) => void;
  getJobsByPage: (currentPageSize?: number, currentPageNumber?: number) => void;
}

export const PagingBar: React.FunctionComponent<IPagingBarProps> = (
  props: IPagingBarProps
) => {

  const { t } = useTranslation();
  const { pageSize, pageNumber, totalNumberOfPages, setPageSize, setPageNumber, setPageSizeCookie, getJobsByPage } = props;

  const pageSizeOptions: IDropdownOption[] = [
    { key: '10', text: '10' },
    { key: '20', text: '20' },
    { key: '30', text: '30' },
    { key: '40', text: '40' },
    { key: '50', text: '50' },
  ];

  const leftLabelMessage: React.CSSProperties = {
    marginRight: 5
  }

  const rightLabelMessage: React.CSSProperties = {
    marginLeft: 5
  }

  const divContainer: React.CSSProperties = {
    display: "flex",
    alignItems: "center",
    marginLeft: 10,
    marginRight: 10
  }

  const leftButtonProps: IButtonProps = {
    iconProps: {
      iconName: 'ChevronLeft',
    },
    title: 'Prev',
  };

  const righttButtonProps: IButtonProps = {
    iconProps: {
      iconName: 'ChevronRight',
    },
    title: 'Next',
  };

  const mainContainer: React.CSSProperties = {
    display: "flex",
    justifyContent: "flex-end",
    marginTop: 10
  }

  const onPageSizeChanged = (event: React.FormEvent<HTMLDivElement>, item: IDropdownOption | undefined): void => {
    if (item) {
      setPageSize(item.key.toString());
      setPageNumber(1);
      setPageSizeCookie(item.key.toString());
      getJobsByPage(parseInt(item.key.toString()), 1);
    }
  }

  const navigateToPage = (direction: number) => {
    if (pageNumber + direction === 0 || totalNumberOfPages === undefined || pageNumber + direction > totalNumberOfPages)
      return;

    let newPageNumber = pageNumber + direction;
    setPageNumber(newPageNumber);
    getJobsByPage(parseInt(pageSize), newPageNumber);
  }

  const onPageNumberChanged = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string | undefined): void => {
    if (newValue === undefined || newValue === ''
      || isNaN(parseInt(newValue))
      || parseInt(newValue) <= 0
      || (totalNumberOfPages !== undefined && !isNaN(totalNumberOfPages) && parseInt(newValue) > totalNumberOfPages))
      return;

    setPageNumber(parseInt(newValue));
    getJobsByPage(parseInt(pageSize), parseInt(newValue));
  }

  return (
    <div style={mainContainer}>
      <div style={divContainer}>
        <IconButton
          {...leftButtonProps}
          onClick={() => navigateToPage(-1)}
        />
        <label>{t('JobsList.PagingBar.previousPage')}</label>
        <div style={divContainer}>
          <label style={leftLabelMessage}>{t('JobsList.PagingBar.page')}</label>
          <TextField
            style={{ width: 55 }}
            value={pageNumber.toString()}
            onChange={onPageNumberChanged}
          />
          <label style={rightLabelMessage}>{t('JobsList.PagingBar.of')} {(totalNumberOfPages ? totalNumberOfPages : 1)}</label>
        </div>
        <label>{t('JobsList.PagingBar.nextPage')}</label>
        <IconButton
          {...righttButtonProps}
          onClick={() => navigateToPage(1)}
        />
      </div>
      <div style={divContainer}>
        <label style={leftLabelMessage}>{t('JobsList.PagingBar.display')}</label>
        <Dropdown
          options={pageSizeOptions}
          defaultSelectedKey={pageSize}
          onChange={onPageSizeChanged}
        />
        <label style={rightLabelMessage}>{t('JobsList.PagingBar.items')}</label>
      </div>
    </div >
  )
}