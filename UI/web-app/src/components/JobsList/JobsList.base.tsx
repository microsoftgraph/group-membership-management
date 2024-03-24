// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  DetailsListLayoutMode,
  IColumn,
  SelectionMode,
} from '@fluentui/react/lib/DetailsList';
import { useEffect, useState } from 'react';
import { useSelector, useDispatch } from 'react-redux';
import { fetchJobs } from '../../store/jobs.api';
import {
  selectAllJobs,
  selectGetJobsError,
  setGetJobsError
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
} from '../../store/pagingBar.slice';
import { resetManageMembership } from '../../store/manageMembership.slice';

import { selectIsJobTenantWriter } from '../../store/roles.slice';

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

  const getJobsByPage = (): void => {
    setIsShimmerEnabled(true);
    dispatch(fetchJobs(pagingOptions));
  };

  useEffect(() => {
    dispatch(setPagingBarVisible(true));
  }, [dispatch]);

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
  const items = jobs;
  const env = process.env.REACT_APP_ENVIRONMENT_ABBREVIATION?.toLowerCase();
  const isLowerEnvironment = env !== 'prodv2';
  const isOnboardingEnabled = isLowerEnvironment || isTenantJobWriter;

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
        ? (b[sortKey] || '').toString().localeCompare((a[sortKey] || '').toString())
        : (a[sortKey] || '').toString().localeCompare((b[sortKey] || '').toString());
    }
    return 0;
  });

  const onContextualItemClicked = (
    ev?: React.MouseEvent | React.KeyboardEvent,
    item?: IContextualMenuItem
  ): void => {
    if(item!.key === 'addSync') {
      dispatch(resetManageMembership());
      navigate('/ManageMembership', { replace: false, state: { item: 1 } });
    }
  };

  function onColumnHeaderClick(event?: any, column?: IColumn) {
    if (column) {
      const isSortedDescending: boolean = !!column.isSorted && !column.isSortedDescending;
      dispatch(setSortKey(column.key));
      dispatch(setIsSortedDescending(isSortedDescending));
    }
  }

  const error = useSelector(selectGetJobsError);

  const onDismiss = (): void => {
    dispatch(setGetJobsError());
  };

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

  const menuProps: IContextualMenuProps = {
    items: [
      {
        key: 'addSync',
        text: strings.ManageMembership.addSyncButton,
        iconProps: {iconName: 'AddFriend'},
        onClick: onContextualItemClicked
      },
    ],
    directionalHintFixed: true
  };

  if(isTenantJobWriter){
    menuProps.items[1] = {
      key: 'bulkAddSyncs',
      text: strings.ManageMembership.bulkAddSyncsButton,
      iconProps: {iconName: 'AddGroup'},
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
          <div className={fieldContent ? classNames.enabled : classNames.disabled }>
            {fieldContent ? strings.JobDetails.labels.enabled : strings.JobDetails.labels.disabled}
          </div>
        );

      case 'actionRequired':
        return (
          fieldContent ?
            (fieldContent.includes(ActionRequired.PendingReview) ?
              <div>
                <AlarmClockIcon className={classNames.pendingReviewIcon}/> {fieldContent}
              </div>
              : fieldContent.includes(ActionRequired.SubmissionRejected) ?
                <div>
                  <ErrorBadgeIcon className={classNames.rejectedIcon}/> {fieldContent}
                </div>
                : <div>
                  <ReportHackedIcon className={classNames.actionRequiredIcon}/> {fieldContent}
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
            {isOnboardingEnabled ?
              <PrimaryButton
                text={strings.ManageMembership.manageMembershipButton}
                menuProps={menuProps}
                persistMenu={true}
              >
              </PrimaryButton>
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
    </div>
  );
};
