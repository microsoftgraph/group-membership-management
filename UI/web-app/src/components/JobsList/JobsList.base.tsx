// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  DetailsListLayoutMode,
  DetailsRow,
  IColumn,
  IDetailsRowProps,
  ISelection,
  SelectionMode,
} from '@fluentui/react/lib/DetailsList';
import { useEffect, useRef, useState } from 'react';
import { useSelector, useDispatch } from 'react-redux';
import { downloadJobs, fetchJobs } from '../../store/jobs.api';
import {
  selectAllJobs,
  selectGetJobsError,
  setGetJobsError,
  clearJob,
  selectJobsToDownload,
  downloadJobsLoading,
  clearJobsToDownload
} from '../../store/jobs.slice';
import { AppDispatch } from '../../store';

import { Selection, IObjectWithKey, Stack } from '@fluentui/react';
import { useNavigate } from 'react-router-dom';
import {
  classNamesFunction,
  IProcessedStyleSet,
  MessageBar,
  MessageBarType,
  IconButton,
  IIconProps,
  PrimaryButton,
  IContextualMenuProps,
  IContextualMenuItem
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
  AlarmClockIcon,
  ErrorBadgeIcon,
} from '@fluentui/react-icons-mdl2';
import { JobsListFilter } from '../JobsListFilter/JobsListFilter';
import { ActionRequired, PagingOptions } from '../../models';
import { useStrings } from '../../store/hooks';
import {
  selectPagingBarPageNumber,
  selectPagingBarPageSize,
  selectPagingOptions,
  selectPagingBarSortKey,
  selectPagingBarIsSortedDescending,
  setSortKey,
  setIsSortedDescending,
  selectPagingBarFilterStatus,
  selectPagingBarFilterActionRequired,
  selectPagingBarfilterDestinationId,
  selectPagingBarfilterDestinationName,
  selectPagingBarfilterDestinationType,
  selectPagingBarfilterDestinationOwner,
  setPagingBarVisible,
  setCustomSortBy,
} from '../../store/pagingBar.slice';
import { resetManageMembership } from '../../store/manageMembership.slice';

import { selectIsJobTenantWriter, selectIsJobWriter } from '../../store/roles.slice';

const getClassNames = classNamesFunction<
  IJobsListStyleProps,
  IJobsListStyles
>();

interface IItem extends IObjectWithKey {
  syncJobId?: string;
}

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

  const pageNumber: number = useSelector(selectPagingBarPageNumber);
  const pageSize: string = useSelector(selectPagingBarPageSize);
  const pagingOptions: PagingOptions = useSelector(selectPagingOptions);
  const sortKey: string | undefined = useSelector(selectPagingBarSortKey);
  const isSortedDescending: boolean | undefined = useSelector(selectPagingBarIsSortedDescending);
  const filterStatus: string | undefined = useSelector(selectPagingBarFilterStatus);
  const filterActionRequired: string | undefined = useSelector(selectPagingBarFilterActionRequired);
  const filterDestinationId: string | undefined = useSelector(selectPagingBarfilterDestinationId);
  const filterDestinationName: string | undefined = useSelector(selectPagingBarfilterDestinationName);
  const filterDestinationType: string | undefined = useSelector(selectPagingBarfilterDestinationType);
  const filterDestinationOwner: string | undefined = useSelector(selectPagingBarfilterDestinationOwner);
  const isTenantJobWriter: boolean | undefined = useSelector(selectIsJobTenantWriter);
  const isJobWriter: boolean | undefined = useSelector(selectIsJobWriter);
  const [selectedItems, setSelectedItems] = useState<IItem[]>([]);
  const jobsToDownloadLoading = useSelector(downloadJobsLoading);
  const jobsToDownload = useSelector(selectJobsToDownload) ?? '';

  const selectionRef = useRef<ISelection<IObjectWithKey>>(
    new Selection<IItem>({
      getKey: (item) => item.syncJobId || '',
      onSelectionChanged: () => {
        const selected = selectionRef.current.getSelection();
        setSelectedItems(selected as IItem[]);
      }
    }) as ISelection<IObjectWithKey>
  );

  const clearSelection = () => {
    selectionRef.current.setItems(items, true);
    selectionRef.current.setAllSelected(false);
    setSelectedItems([]);
  };

  const getJobsByPage = (): void => {
    setIsShimmerEnabled(true);
    dispatch(fetchJobs(pagingOptions));
  };

  useEffect(() => {
    dispatch(setPagingBarVisible(true));
  }, [dispatch]);

  useEffect(() => {
    if (!jobsToDownload) return;
    const header = Object.keys(jobsToDownload[0]).join(',');
    const rows = jobsToDownload.map(item =>
      Object.values(item).map(val =>
        `"${String(val).replace(/"/g, '""')}"`
      ).join(',')
    );
    const csvContent = [header, ...rows].join('\r\n');
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', 'selected-items.csv');
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
    dispatch(clearJobsToDownload());
    clearSelection();
  }, [dispatch, jobsToDownload]);

  useEffect(() => {
    dispatch(fetchJobs(pagingOptions));
  }, [pageNumber,
    pageSize,
    sortKey,
    isSortedDescending,
    filterStatus,
    filterActionRequired,
    filterDestinationId,
    filterDestinationName,
    filterDestinationType,
    filterDestinationOwner
  ]);

  const navigate = useNavigate();

  const [isShimmerEnabled, setIsShimmerEnabled] = useState(false);
  const items: IItem[] = (jobs || []).map(job => ({
    ...job,
    key: job.syncJobId,
  }));
  const columns = [
    {
      key: 'targetGroupType',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.type,
      fieldName: 'targetGroupType',
      minWidth: 100,
      maxWidth: 100,
      isMultiline: false,
      isResizable: true,
      isSorted: sortKey === 'targetGroupType',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'targetGroupName',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.name,
      fieldName: 'targetGroupName',
      minWidth: 250,
      isMultiline: false,
      isResizable: true,
      isSorted: sortKey === 'targetGroupName',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    },
    {
      key: 'email',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.email, 
      fieldName: 'email',
      minWidth: 250,
      isMultiline: false,
      isResizable: true,
      isSorted: sortKey === 'email',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'lastSuccessfulRunTime',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.lastRun,
      fieldName: 'lastSuccessfulRunTime',
      minWidth: 100,
      maxWidth: 100,
      isMultiline: true,
      isResizable: true,
      isSorted: sortKey === 'lastSuccessfulRunTime',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    },
    {
      key: 'estimatedNextRunTime',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.nextRun,
      fieldName: 'estimatedNextRunTime',
      minWidth: 100,
      maxWidth: 100,
      isMultiline: true,
      isResizable: true,
      isSorted: sortKey === 'estimatedNextRunTime',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'enabledOrNot',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.status,
      fieldName: 'enabledOrNot',
      minWidth: 100,
      maxWidth: 100,
      isMultiline: false,
      isResizable: true,
      isSorted: sortKey === 'enabledOrNot',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'actionRequired',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.actionRequired,
      fieldName: 'actionRequired',
      minWidth: 150,
      isMultiline: false,
      isResizable: true,
      isSorted: sortKey === 'actionRequired',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'arrow',
      name: '',
      fieldName: '',
      minWidth: 50,
      isMultiline: false,
      isResizable: true,
      columnActionsMode: 0,
    },
  ];

  const onContextualItemClicked = (
    ev?: React.MouseEvent | React.KeyboardEvent,
    item?: IContextualMenuItem
  ): void => {
    if (item!.key === 'addSync') {
      dispatch(resetManageMembership());
      dispatch(clearJob());
      navigate('/ManageMembership', { replace: false, state: { item: 1 } });
    }
  };

  function onColumnHeaderClick(event?: any, column?: IColumn) {
    if (column) {
      const isSortedDescending: boolean = !!column.isSorted && !column.isSortedDescending;
      dispatch(setSortKey(column.key));
      dispatch(setIsSortedDescending(isSortedDescending));

      if (column.key === 'targetGroupName') {
        dispatch(setCustomSortBy(column.key));
      }
    }
  }

  const error = useSelector(selectGetJobsError);

  const onDismiss = (): void => {
    dispatch(setGetJobsError());
  };

  const onItemInvoked = (
    item?: any,
    index?: number,
    ev?: Event
  ): void => {
    if(item.targetGroupName === null){
      navigate('/NotFound', { replace: true, state: { item: item} });
    }
    if (item && item.syncJobId) {
      navigate(`/JobDetails/${item.syncJobId}`);
    }
    const selectedItems = selectionRef.current.getSelection();
    if (!selectedItems.includes(item)) {
      selectionRef.current.setItems(
        [...selectedItems, item] as IObjectWithKey[],
        false
      );
    }
  };

  const onRenderRow = (props?: IDetailsRowProps): JSX.Element => {
    if (!props) return <></>;
  
    const { item } = props;
    const handleRowClick = (event: React.MouseEvent<HTMLDivElement, MouseEvent>): void => {
      const target = event.target as HTMLElement;
      if (
        target.closest('.ms-DetailsRow-cellCheck') ||
        target.closest('button')
      ) {
        return;
      }
      if (item?.targetGroupName === null) {
        navigate('/NotFound', { replace: true, state: { item } });
      } else if (item?.syncJobId) {
        navigate(`/JobDetails/${item.syncJobId}`);
      }
    };
  
    return (
      <div
        onClick={handleRowClick}
        style={{ cursor: 'pointer' }}
        role="button"
      >
        <DetailsRow {...props} />
      </div>
    );
  };

  const onRefreshClicked = (
    item?: any,
    index?: number,
    ev?: React.FocusEvent<HTMLElement>
  ): void => {
    dispatch(fetchJobs());
  };

  const menuProps: IContextualMenuProps = {
    items: [
      {
        key: 'addSync',
        text: strings.ManageMembership.addSyncButton,
        iconProps: { iconName: 'AddFriend' },
        onClick: onContextualItemClicked
      },
    ],
    directionalHintFixed: true
  };

  if (isTenantJobWriter) {
    menuProps.items[1] = {
      key: 'bulkAddSyncs',
      text: strings.ManageMembership.bulkAddSyncsButton,
      iconProps: { iconName: 'AddGroup' },
      disabled: true
    };
  }

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
          <div className={fieldContent ? classNames.enabled : classNames.disabled}>
            {fieldContent ? strings.JobDetails.labels.enabled : strings.JobDetails.labels.disabled}
          </div>
        );

      case 'actionRequired':
        return (
          fieldContent ?
            (fieldContent.includes(ActionRequired.PendingReview) ?
              <div>
                <AlarmClockIcon className={classNames.pendingReviewIcon} /> {fieldContent}
              </div>
              : fieldContent.includes(ActionRequired.SubmissionRejected) ?
                <div>
                  <ErrorBadgeIcon className={classNames.rejectedIcon} /> {fieldContent}
                </div>
                : <div>
                  <ReportHackedIcon className={classNames.actionRequiredIcon} /> {fieldContent}
                </div>
            )
            : <></>
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

  const handleDownloadButtonClick = () => {
    if (selectedItems.length === 0) return;
    dispatch(downloadJobs(selectedItems.map(item => item.syncJobId).filter((id): id is string => id !== undefined)));
  };

  return (
    <div className={classNames.root}>
      <div className={classNames.jobsListFilter}>
        <JobsListFilter
          getJobsByPage={getJobsByPage}
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
            {isJobWriter && 
              <div className={classNames.header}>
              <div>
              <PrimaryButton
                text={jobsToDownloadLoading ? strings.ManageMembership.downloadingButton : strings.ManageMembership.downloadButton}
                onClick={handleDownloadButtonClick}
                disabled={selectedItems.length === 0 || jobsToDownloadLoading}
              />
              </div>
              <div className={classNames.manageMembershipButton}>
              <PrimaryButton
                text={strings.ManageMembership.manageMembershipButton}
                menuProps={menuProps}
                persistMenu={true}
              />
              </div>
              </div>
            }
          </div>
          <div className={classNames.tabContent}>
            <ShimmeredDetailsList
              setKey="set"
              onColumnHeaderClick={onColumnHeaderClick}
              items={items || []}
              columns={columns}
              enableShimmer={!jobs || isShimmerEnabled}
              layoutMode={DetailsListLayoutMode.justified}
              selectionMode={SelectionMode.multiple}
              ariaLabelForShimmer="Content is being fetched"
              ariaLabelForGrid="Item details"
              selectionPreservedOnEmptyClick={true}
              ariaLabelForSelectionColumn={strings.JobsList.ShimmeredDetailsList.toggleSelection}
              ariaLabelForSelectAllCheckbox={strings.JobsList.ShimmeredDetailsList.toggleAllSelection}
              checkButtonAriaLabel={strings.JobsList.ShimmeredDetailsList.selectRow}
              onRenderItemColumn={_renderItemColumn}
              onItemInvoked={onItemInvoked} // Handle tab and enter key navigation
              onRenderRow={onRenderRow} // Handle row click
              selection={selectionRef.current}
            />

            {jobs?.length === 0 && (
              <div className={classNames.noMembershipsFoundText}>
                <Text variant="medium">{strings.JobsList.NoResults}</Text>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
