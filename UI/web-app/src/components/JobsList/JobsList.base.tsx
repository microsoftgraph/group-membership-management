// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  DetailsListLayoutMode,
  DetailsRow,
  IColumn,
  IDetailsRowProps,
  ISelection,
  SelectionMode,
  ColumnActionsMode,
} from '@fluentui/react/lib/DetailsList';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSelector, useDispatch } from 'react-redux';
import { approveJobs, downloadJobs, fetchJobs, getPeoplePickerSuggestions } from '../../store/jobs.api';
import {
  selectAllJobs,
  selectGetJobsError,
  setGetJobsError,
  clearJob,
  selectJobsToDownload,
  downloadJobsLoading,
  clearJobsToDownload,
  selectApproveJobsLoading,
  selectNumberOfApprovedJobs,
  selectNumberOfJobs,
  selectApproveJobsError,
  setApproveJobsResponse,
  setApproveJobsLoading,
  selectJobsLoading
} from '../../store/jobs.slice';
import { AppDispatch } from '../../store';

import {
  Selection,
  IObjectWithKey,
  Stack,
  Label,
  ProgressIndicator,
  Panel,
  PanelType,
  Spinner,
  SpinnerSize,
  Icon,
  SearchBox,
  NormalPeoplePicker,
  DirectionalHint,
  Checkbox
} from '@fluentui/react';
import { IPersonaProps } from '@fluentui/react/lib/Persona';
import { useNavigate } from 'react-router-dom';
import {
  classNamesFunction,
  IProcessedStyleSet,
  MessageBar,
  MessageBarType,
  IconButton,
  IIconProps,
  PrimaryButton,
  DefaultButton,
  IContextualMenuProps,
  IContextualMenuItem,
  ContextualMenu
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
  HourGlassIcon,
} from '@fluentui/react-icons-mdl2';
import { ActionRequired, SyncStatus } from '../../models';
import { useStrings } from '../../store/hooks';
import {
  selectPagingOptions,
  selectPagingBarSortKey,
  selectPagingBarIsSortedDescending,
  selectPagingBarfilterDestinationName,
  selectPagingBarfilterDestinationOwnerPersona,
  setSortKey,
  setIsSortedDescending,
  setPagingBarVisible,
  setCustomSortBy,
  setFilterDestinationName,
  setFilterDestinationOwnerPersona,
  setFilterStatus,
  setFilterActionRequired,
  selectPagingBarFilterStatus,
  selectPagingBarFilterActionRequired,
  resetFilters,
} from '../../store/pagingBar.slice';
import { resetManageMembership } from '../../store/manageMembership.slice';

import Papa from 'papaparse';
import { selectIsJobTenantWriter, selectIsJobWriter, selectIsSubmissionReviewer, selectIsSubmissionRejector } from '../../store/roles.slice';
import { destinationTypeLocalization } from '../../utils/destinationTypeUtils';
import { debounce, getDisplayActionRequired } from '../../utils/jobUtils';

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

  const theme = useTheme();
  const classNames: IProcessedStyleSet<IJobsListStyles> = getClassNames(
    styles,
    {
      className,
      theme,
    }
  );

  const strings = useStrings();

  const dispatch = useDispatch<AppDispatch>();
  const jobs = useSelector(selectAllJobs);
  const jobsLoading = useSelector(selectJobsLoading);

  const pagingOptions = useSelector(selectPagingOptions);
  const sortKey: string | undefined = useSelector(selectPagingBarSortKey);
  const isSortedDescending: boolean | undefined = useSelector(selectPagingBarIsSortedDescending);

  const isTenantJobWriter: boolean | undefined = useSelector(selectIsJobTenantWriter);
  const isJobWriter: boolean | undefined = useSelector(selectIsJobWriter);
  const isSubmissionReviewer = useSelector(selectIsSubmissionReviewer);
  const isSubmissionRejector = useSelector(selectIsSubmissionRejector);
  const [csvErrorMessage, setCsvErrorMessage] = useState<string>('');
  const [selectedItems, setSelectedItems] = useState<IItem[]>([]);
  const jobsToDownloadLoading = useSelector(downloadJobsLoading);
  const jobsToDownload = useSelector(selectJobsToDownload) ?? '';
  const approveJobsLoading = useSelector(selectApproveJobsLoading);
  const numberOfApprovedJobs = useSelector(selectNumberOfApprovedJobs);
  const numberOfJobs = useSelector(selectNumberOfJobs);
  const approveJobsError = useSelector(selectApproveJobsError);

  // Filter state
  const persistedFilterDestinationName = useSelector(selectPagingBarfilterDestinationName);
  const persistedFilterDestinationOwnerPersona = useSelector(selectPagingBarfilterDestinationOwnerPersona);
  const [searchValue, setSearchValue] = useState<string>(persistedFilterDestinationName || '');
  const [selectedOwners, setSelectedOwners] = useState<IPersonaProps[]>(() => {
    return persistedFilterDestinationOwnerPersona ? [persistedFilterDestinationOwnerPersona] : [];
  });

  const handleSearchChanged = (_event?: React.ChangeEvent<HTMLInputElement>, newValue?: string): void => {
    const value = newValue || '';
    setSearchValue(value);
    debouncedDispatchDestinationName(value);
  };

  const debouncedDispatchDestinationName = useMemo(
    () => debounce((value: string) => {
      dispatch(setFilterDestinationName(value));
    }, 350),
    [dispatch]
  );

  useEffect(() => {
    return () => {
      debouncedDispatchDestinationName.cancel();
    };
  }, [debouncedDispatchDestinationName]);

  const handleSearchClear = (): void => {
    debouncedDispatchDestinationName.cancel();
    setSearchValue('');
    dispatch(setFilterDestinationName(''));
  };

  const handleOwnersChanged = (items?: IPersonaProps[] | undefined) => {
    if (items !== undefined && items.length > 0) {
      setSelectedOwners(items);
      const persona = items[0];
      const personaKey = typeof persona.key === 'number' ? persona.key : parseInt(persona.key as string, 10);
      dispatch(setFilterDestinationOwnerPersona({
        key: personaKey,
        text: persona.text || '',
        secondaryText: persona.secondaryText || '',
        id: persona.id as string
      }));
    } else {
      setSelectedOwners([]);
      dispatch(setFilterDestinationOwnerPersona(undefined));
    }
  };

  const getPickerSuggestions = async (filterText: string): Promise<IPersonaProps[]> => {
    if (!filterText) return [];
    const action = await dispatch(getPeoplePickerSuggestions(filterText));
    if (getPeoplePickerSuggestions.rejected.match(action)) {
      return [];
    }
    return (action.payload as IPersonaProps[]) ?? [];
  };

  const clearFilters = () => {
    debouncedDispatchDestinationName.cancel();
    dispatch(resetFilters());
    setSearchValue('');
    setSelectedOwners([]);
  };

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
    const numberOfJobs = rows.length;
    const csvContent = [header, ...rows].join('\r\n');
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    const now = new Date();
    const pad = (n: number) => n.toString().padStart(2, '0');
    const yyyyMMdd = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}`;
    link.setAttribute('download', `selected-items_${yyyyMMdd}_${numberOfJobs}.csv`);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
    dispatch(clearJobsToDownload());
    clearSelection();
  }, [dispatch, jobsToDownload]);

  // Fetch jobs whenever paging/filter/sort options change. Enable shimmer if a fetch is starting.
  useEffect(() => {
    setIsShimmerEnabled(true);
    dispatch(fetchJobs(pagingOptions));
  }, [dispatch, pagingOptions]);

  // Disable shimmer once loading completes (either success or empty results)
  useEffect(() => {
    if (!jobsLoading) {
      setIsShimmerEnabled(false);
    }
  }, [jobsLoading]);

   const navigate = useNavigate();

  const [isShimmerEnabled, setIsShimmerEnabled] = useState(false);
  const [columnMenuTarget, setColumnMenuTarget] = useState<HTMLElement | null>(null);
  const [columnMenuColumn, setColumnMenuColumn] = useState<IColumn | null>(null);
  const items: IItem[] = (jobs || []).map(job => ({
    ...job,
    key: job.syncJobId,
  }));

  const columns = [
    {
      key: 'targetDestinationType',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.type,
      fieldName: 'targetDestinationType',
      minWidth: 60,
      maxWidth: 100,
      isMultiline: false,
      isResizable: true,
      isSorted: sortKey === 'targetDestinationType',
      isSortedDescending,
      columnActionsMode: 0,
      onRender: (item: any) => {
        const localizedType = destinationTypeLocalization[item.targetDestinationType] || item.targetDestinationType;
        return <span>{localizedType}</span>;
      },
    },
    {
      key: 'targetGroupName',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.name,
      fieldName: 'targetGroupName',
      minWidth: 300,
      isMultiline: false,
      isResizable: true,
      isSorted: sortKey === 'targetGroupName',
      isSortedDescending,
      columnActionsMode: ColumnActionsMode.hasDropdown,
    },
    {
      key: 'email',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.email,
      fieldName: 'email',
      minWidth: 300,
      isMultiline: false,
      isResizable: true,
      isSorted: sortKey === 'email',
      isSortedDescending,
      columnActionsMode: 0,
    },
    {
      key: 'lastModifiedTime',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.lastModified,
      fieldName: 'lastModifiedTime',
      minWidth: 150,
      maxWidth: 170,
      isMultiline: true,
      isResizable: true,
      isSorted: sortKey === 'lastModifiedTime',
      isSortedDescending,
      columnActionsMode: ColumnActionsMode.hasDropdown,
    },
    {
      key: 'lastSuccessfulRunTime',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.lastRun,
      fieldName: 'lastSuccessfulRunTime',
      minWidth: 150,
      maxWidth: 170,
      isMultiline: true,
      isResizable: true,
      isSorted: sortKey === 'lastSuccessfulRunTime',
      isSortedDescending,
      columnActionsMode: ColumnActionsMode.hasDropdown,
    },
    {
      key: 'estimatedNextRunTime',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.nextRun,
      fieldName: 'estimatedNextRunTime',
      minWidth: 150,
      maxWidth: 170,
      isMultiline: true,
      isResizable: true,
      isSorted: sortKey === 'estimatedNextRunTime',
      isSortedDescending,
      columnActionsMode: ColumnActionsMode.hasDropdown,
    },
    {
      key: 'enabledOrNot',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.status,
      fieldName: 'enabledOrNot',
      minWidth: 100,
      maxWidth: 150,
      isMultiline: false,
      isResizable: true,
      isSorted: sortKey === 'enabledOrNot',
      isSortedDescending,
      columnActionsMode: ColumnActionsMode.hasDropdown,
    },
    {
      key: 'actionRequired',
      name: strings.JobsList.ShimmeredDetailsList.columnNames.actionRequired,
      fieldName: 'actionRequired',
      minWidth: 200,
      maxWidth: 250,
      isMultiline: false,
      isResizable: true,
      isSorted: sortKey === 'actionRequired',
      isSortedDescending,
      columnActionsMode: ColumnActionsMode.hasDropdown,
    }
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

    if (item!.key === 'bulkApproveSyncs') {
      setIsPanelOpen(true);
    }
  };

  const dropdownSortColumns = ['targetGroupName', 'lastSuccessfulRunTime', 'estimatedNextRunTime', 'lastModifiedTime'];
  const dropdownColumns = [...dropdownSortColumns, 'enabledOrNot', 'actionRequired'];

  const persistedFilterStatus = useSelector(selectPagingBarFilterStatus);
  const persistedFilterActionRequired = useSelector(selectPagingBarFilterActionRequired);
  const hasActiveFilter = !!(persistedFilterDestinationName || persistedFilterDestinationOwnerPersona || persistedFilterStatus || persistedFilterActionRequired);

  function onColumnHeaderClick(event?: any, column?: IColumn) {
    if (column) {
      if (dropdownColumns.includes(column.key)) {
        setColumnMenuTarget(event?.currentTarget as HTMLElement);
        setColumnMenuColumn(column);
        return;
      }

      const isSortedDescending: boolean = !!column.isSorted && !column.isSortedDescending;
      dispatch(setSortKey(column.key));
      dispatch(setIsSortedDescending(isSortedDescending));
    }
  }

  const applyColumnSort = (column: IColumn, descending: boolean) => {
    dispatch(setSortKey(column.key));
    dispatch(setIsSortedDescending(descending));
    if (column.key === 'targetGroupName' || column.key === 'lastModifiedTime') {
      dispatch(setCustomSortBy(column.key));
    }
    setIsShimmerEnabled(true);
    setColumnMenuTarget(null);
    setColumnMenuColumn(null);
  };

  const applyStatusFilter = (status: string) => {
    dispatch(setFilterStatus(status));
    setColumnMenuTarget(null);
    setColumnMenuColumn(null);
  };

  const applyActionRequiredFilter = (actionRequired: string) => {
    dispatch(setFilterActionRequired(actionRequired));
    setColumnMenuTarget(null);
    setColumnMenuColumn(null);
  };

  const isDateColumn = (key: string) =>
    key === 'lastSuccessfulRunTime' || key === 'estimatedNextRunTime' || key === 'lastModifiedTime';

  const error = useSelector(selectGetJobsError);

  const onDismiss = (): void => {
    dispatch(setGetJobsError());
  };

  const onItemInvoked = (
    item?: any,
    index?: number,
    ev?: Event
  ): void => {
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
      if (item?.syncJobId) {
        navigate(`/JobDetails/${item.syncJobId}`);
      }
    };

    return (
      <div
        onClick={handleRowClick}
        style={{ cursor: 'pointer' }}
        role="presentation"
        data-testid={`job-row-${item.syncJobId}`}
        data-group-name={item.targetGroupName}
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


  const checkboxIconStyle = { styles: { root: { color: theme.semanticColors.bodyText, borderRadius: 2 } } };

  const roundedCheckboxStyles = {
    checkbox: { borderRadius: 4, width: 16, height: 16 },
    checkmark: { fontSize: 12 },
    root: { pointerEvents: 'none' as const },
  };

  const renderCheckboxIcon = (checked: boolean) => () => (
    <Checkbox checked={checked} styles={roundedCheckboxStyles} />
  );

  const menuProps: IContextualMenuProps = {
    items: [],
    directionalHintFixed: true,
    alignTargetEdge: true
  };

  if (isTenantJobWriter) {
    menuProps.items.push({
      key: 'bulkAddSyncs',
      text: strings.ManageMembership.bulkAddSyncsButton,
      iconProps: { iconName: 'AddGroup' },
      disabled: true
    });
  }

  if (isSubmissionReviewer) {
    menuProps.items.push({
      key: 'bulkApproveSyncs',
      text: strings.ManageMembership.bulkApproveSyncsButton,
      iconProps: { iconName: 'CheckMark' },
      onClick: onContextualItemClicked
    });
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
      case 'estimatedNextRunTime': {
        const isEmpty = !fieldContent || fieldContent === '';
        const spaceIndex = isEmpty ? -1 : fieldContent.indexOf(' ');
        const lastOrNextRunDate = isEmpty
          ? '-'
          : fieldContent.substring(0, spaceIndex).replace(',', '');
        const hoursAgoOrHoursLeft = isEmpty
          ? ''
          : fieldContent.substring(spaceIndex + 1).replace(',', '');

        return (
          <div>
            <div>{lastOrNextRunDate}</div>
            <div style={{ color: theme.palette.neutralTertiary, fontSize: 10 }}>{hoursAgoOrHoursLeft}</div>
          </div>
        );
      }

      case 'lastModifiedTime': {
        const isEmpty = !fieldContent || fieldContent === '';
        if (isEmpty) {
          return <div>-</div>;
        }

        try {
          const utcDate = fieldContent.endsWith('Z') ? fieldContent : `${fieldContent}Z`;
          const date = new Date(utcDate);
          const now = new Date();
          const diffMs = now.getTime() - date.getTime();
          const diffHours = Math.round(diffMs / (1000 * 60 * 60));
          const dateStr = `${String(date.getMonth() + 1).padStart(2, '0')}/${String(date.getDate()).padStart(2, '0')}/${date.getFullYear()}`;
          const hoursAgo = diffHours <= 0 ? strings.JobsList.JobsListFilter.columnMenu.justNow : strings.JobsList.JobsListFilter.columnMenu.hrsAgo.replace('{0}', String(diffHours));

          return (
            <div>
              <div>{dateStr}</div>
              <div style={{ color: theme.palette.neutralTertiary, fontSize: 10 }}>{hoursAgo}</div>
            </div>
          );
        } catch {
          return <div>{fieldContent}</div>;
        }
      }

      case 'enabledOrNot':
        return (
          <div className={fieldContent ? classNames.enabled : classNames.disabled}>
            {fieldContent ? strings.JobDetails.labels.enabled : strings.JobDetails.labels.disabled}
          </div>
        );

      case 'actionRequired': {
        const displayActionRequired = getDisplayActionRequired(item, isSubmissionReviewer || isSubmissionRejector);
        return (
          displayActionRequired ?
            (displayActionRequired === ActionRequired.PendingReview ?
              <div>
                <AlarmClockIcon className={classNames.pendingReviewIcon} /> {displayActionRequired}
              </div>
              : displayActionRequired === ActionRequired.PendingConfiguration ?
                <div>
                  <HourGlassIcon className={classNames.pendingReviewIcon} /> {displayActionRequired}
                </div>
                : displayActionRequired === ActionRequired.PendingAutoApproval ?
                  <div>
                    <HourGlassIcon className={classNames.pendingReviewIcon} /> {displayActionRequired}
                  </div>
                : displayActionRequired === ActionRequired.SubmissionRejected ?
                  <div>
                    <ErrorBadgeIcon className={classNames.rejectedIcon} /> {displayActionRequired}
                  </div>
                  : <div>
                    <ReportHackedIcon className={classNames.actionRequiredIcon} /> {displayActionRequired}
                  </div>
            )
            : <></>
        );
      }

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

  const handleBulkApproveButtonClick = async () => {
    await dispatch(approveJobs({
      jobIdsToApprove: uploadedJobIdsToApprove,
      totalNumberOfJobs: uploadedJobsCount ?? 0
    }));
  };

  const fileInputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [uploaded, setUploaded] = useState(false);
  const [progress, setProgress] = useState(0);
  const [uploadedJobIdsToApprove, setUploadedJobIdsToApprove] = useState<string[]>([]);
  const [uploadedJobsCount, setUploadedJobsCount] = useState<number>();
  const [fileName, setFileName] = useState<string | null>(null);
  const [isPanelOpen, setIsPanelOpen] = useState(false);

  const onDismissPanel = useCallback(() => {
    setIsPanelOpen(false);
    setFileName('');
    setUploading(false);
    setUploaded(false);
    setProgress(0);
    dispatch(setApproveJobsResponse());
    dispatch(setApproveJobsLoading());

    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  }, []);

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {

    setCsvErrorMessage('');

    const file = event.target.files?.[0];

    if (!file || file.type !== "text/csv") {
      setCsvErrorMessage(strings.ManageMembership.csvErrorMessage);
      return;
    }

    setUploading(true);
    setUploaded(false);
    setProgress(0);
    setFileName(file.name);


    const interval = setInterval(() => {
      setProgress((prev) => {
        const next = prev + 0.1;
        if (next >= 1) {
          clearInterval(interval);
          setUploading(false);
          setUploaded(true);
        }
        return next >= 1 ? 1 : next;
      });
    }, 200);


    Papa.parse(file, {
      header: true,
      skipEmptyLines: true,
      complete: (results) => {
        const data = results.data as Array<Record<string, string>>;
        const ids = data
          .filter(row => row['status'] === SyncStatus.PendingReview)
          .map(row => row['syncJobId'])
          .filter(id => !!id);
        setUploadedJobIdsToApprove(ids);
        setUploadedJobsCount(data.length)
      },
      error: (err: any) => {
        setCsvErrorMessage(strings.ManageMembership.csvErrorMessage);
      }
    });
  };

  return (
    <div className={classNames.root}>
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
            <div className={classNames.header}>
              {(isTenantJobWriter || isSubmissionReviewer) && (
                <div className={classNames.ownerPickerContainer}>
                  <NormalPeoplePicker
                    onResolveSuggestions={getPickerSuggestions}
                    pickerSuggestionsProps={{
                      suggestionsHeaderText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.suggestionsHeaderText,
                      noResultsFoundText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.noResultsFoundText,
                      loadingText: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.loadingText,
                    }}
                    key={'normal'}
                    aria-label={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.label}
                    selectionAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.selectionAriaLabel}
                    removeButtonAriaLabel={strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.removeButtonAriaLabel}
                    resolveDelay={300}
                    itemLimit={1}
                    selectedItems={selectedOwners}
                    onChange={handleOwnersChanged}
                    inputProps={{
                      placeholder: strings.JobsList.JobsListFilter.filters.ownerPeoplePicker.label,
                      style: { paddingRight: 28 },
                    }}
                    styles={{
                      root: classNames.ownerPicker,
                      text: classNames.ownerPickerText,
                    }}
                    pickerCalloutProps={{
                      directionalHint: DirectionalHint.bottomAutoEdge,
                      calloutWidth: 300,
                    }}
                  />
                  <Icon iconName="Contact" className={classNames.inputIcon} />
                </div>
              )}
              <div className={classNames.searchBoxContainer}>
                <SearchBox
                  placeholder={strings.JobsList.JobsListFilter.filters.destinationName.placeholder}
                  value={searchValue}
                  onChange={handleSearchChanged}
                  onClear={handleSearchClear}
                  iconProps={{ iconName: '' }}
                  styles={{
                    root: classNames.searchBox,
                    icon: { display: 'none' },
                    field: { paddingRight: 28 },
                  }}
                />
                <Icon iconName="Search" className={classNames.inputIcon} />
              </div>
              <DefaultButton
                iconProps={{ iconName: 'ClearFilter' }}
                text={strings.JobsList.JobsListFilter.clearButtonTooltip}
                onClick={clearFilters}
                disabled={!hasActiveFilter}
                styles={{
                  root: {
                    borderRadius: 4,
                    borderColor: hasActiveFilter ? theme.palette.themePrimary : theme.palette.neutralTertiaryAlt,
                    color: hasActiveFilter ? theme.palette.themePrimary : theme.palette.neutralTertiary,
                  },
                  icon: {
                    color: hasActiveFilter ? theme.palette.themePrimary : theme.palette.neutralTertiary,
                  },
                }}
              />
              {isTenantJobWriter &&
                <div>
                  <DefaultButton
                    text={jobsToDownloadLoading ? strings.ManageMembership.downloadingButton : strings.ManageMembership.downloadButton}
                    iconProps={{ iconName: 'Download' }}
                    onClick={handleDownloadButtonClick}
                    disabled={selectedItems.length === 0 || jobsToDownloadLoading}
                    styles={{ root: { borderRadius: 4 } }}
                  />
                <>
                  <input
                    id="file-uploader"
                    type="file"
                    accept=".csv"
                    ref={fileInputRef}
                    onChange={handleFileChange}
                    style={{ display: "none" }}
                  />
                  <Panel
                    type={PanelType.medium}
                    isOpen={isPanelOpen}
                    onDismiss={onDismissPanel}
                    headerText={strings.ManageMembership.uploadHeader}>
                    <Stack tokens={{ childrenGap: 15 }} style={{ width: 400 }}>
                      <Label>{strings.ManageMembership.selectCSVFileLabel}</Label>
                      <Label htmlFor="file-uploader">
                        <span className={classNames.chooseFileButton}>{strings.ManageMembership.chooseFileButton}</span>
                      </Label>
                      {uploading && (
                        <ProgressIndicator label={strings.ManageMembership.uploadingLabel} percentComplete={progress} />
                      )}
                      {uploaded && (
                        <Label>
                          <Icon iconName="CheckMark" className={classNames.successStatus} /> {strings.ManageMembership.uploadCompleteLabel} <strong>{fileName}</strong>
                        </Label>
                      )}
                      {!numberOfApprovedJobs && (
                        <PrimaryButton
                          text={strings.ManageMembership.approveButton}
                          disabled={!uploaded}
                          onClick={handleBulkApproveButtonClick}
                        />
                      )}
                      {approveJobsLoading && (<Spinner size={SpinnerSize.small} label={strings.HROnboarding.loadingText} />)}
                      {numberOfApprovedJobs !== undefined && (
                        <div>
                        <div className={classNames.jobsHeader}>
                          <div className={classNames.approvedJobsLabel}>
                          <Label>
                          {strings.ManageMembership.totalNumberOfJobsLabel} {numberOfJobs}
                          </Label>
                          </div>
                          <div className={classNames.totalJobsLabel}>
                          <Label>
                          {strings.ManageMembership.totalNumberOfApprovedJobsLabel} {numberOfApprovedJobs}
                          </Label>
                          </div>
                        </div>
                        </div>
                      )}
                      {approveJobsError !== undefined && (
                        <Label>
                          <Icon iconName="ErrorBadge" className={classNames.errorStatus} /> {strings.ManageMembership.approveErrorStatusLabel}
                        </Label>
                      )}
                      {csvErrorMessage !== '' && (
                        <Label>
                          <Icon iconName="ErrorBadge" className={classNames.errorStatus} /> {csvErrorMessage}
                        </Label>
                      )}
                    </Stack>
                  </Panel>
                </>
                </div>
              }
              {isJobWriter &&
                <div className={classNames.manageMembershipButton}>
                  {(isTenantJobWriter || isSubmissionReviewer) ? (
                    <PrimaryButton
                      id="manage-membership-button"
                      text={strings.ManageMembership.manageMembershipButton}
                      menuProps={menuProps}
                      split={true}
                      onClick={() => {
                        dispatch(resetManageMembership());
                        dispatch(clearJob());
                        navigate('/ManageMembership', { replace: false, state: { item: 1 } });
                      }}
                      persistMenu={true}
                      styles={{
                        root: { borderRadius: '4px 0 0 4px' },
                        splitButtonContainer: { borderRadius: 4 },
                        splitButtonMenuButton: { borderRadius: '0 4px 4px 0' },
                        splitButtonDivider: { backgroundColor: theme.palette.themeLight },
                      }}
                    />
                  ) : (
                    <PrimaryButton
                      id="manage-membership-button"
                      text={strings.ManageMembership.manageMembershipButton}
                      onClick={() => {
                        dispatch(resetManageMembership());
                        dispatch(clearJob());
                        navigate('/ManageMembership', { replace: false, state: { item: 1 } });
                      }}
                      styles={{
                        root: { borderRadius: 4 },
                      }}
                    />
                  )}
                </div>
              }
            </div>
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
              onItemInvoked={onItemInvoked}
              onRenderRow={onRenderRow}
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
      {columnMenuTarget && columnMenuColumn && columnMenuColumn.key === 'enabledOrNot' && (
        <ContextualMenu
          target={columnMenuTarget}
          onDismiss={() => { setColumnMenuTarget(null); setColumnMenuColumn(null); }}
          styles={{ root: { fontSize: 12 }, subComponentStyles: { menuItem: { root: { fontSize: 12 } }, callout: {} } }}
          items={[
            {
              key: 'selectAll',
              text: strings.JobsList.JobsListFilter.columnMenu.selectAll,
              style: { color: theme.palette.themePrimary, paddingLeft: 4 },
              iconProps: { iconName: '', styles: { root: { display: 'none' } } },
              onClick: () => applyStatusFilter(''),
            },
            {
              key: 'divider',
              itemType: 1,
            },
            {
              key: 'enabled',
              text: strings.JobsList.JobsListFilter.filters.status.options.enabled,
              onRenderIcon: renderCheckboxIcon(persistedFilterStatus === 'Enabled'),
              onClick: () => applyStatusFilter(persistedFilterStatus === 'Enabled' ? '' : 'Enabled'),
            },
            {
              key: 'disabled',
              text: strings.JobsList.JobsListFilter.filters.status.options.disabled,
              onRenderIcon: renderCheckboxIcon(persistedFilterStatus === 'Disabled'),
              onClick: () => applyStatusFilter(persistedFilterStatus === 'Disabled' ? '' : 'Disabled'),
            },
          ]}
          directionalHint={DirectionalHint.bottomLeftEdge}
        />
      )}
      {columnMenuTarget && columnMenuColumn && columnMenuColumn.key === 'actionRequired' && (
        <ContextualMenu
          target={columnMenuTarget}
          onDismiss={() => { setColumnMenuTarget(null); setColumnMenuColumn(null); }}
          styles={{ root: { fontSize: 12 }, subComponentStyles: { menuItem: { root: { fontSize: 12 } }, callout: {} } }}
          items={[
            {
              key: 'selectAll',
              text: strings.JobsList.JobsListFilter.columnMenu.selectAll,
              style: { color: theme.palette.themePrimary, paddingLeft: 4 },
              iconProps: { iconName: '', styles: { root: { display: 'none' } } },
              onClick: () => applyActionRequiredFilter(''),
            },
            {
              key: 'divider',
              itemType: 1,
            },
            {
              key: SyncStatus.PendingReview,
              text: strings.JobsList.JobsListFilter.filters.actionRequired.options.pendingReview,
              onRenderIcon: renderCheckboxIcon(persistedFilterActionRequired === SyncStatus.PendingReview),
              onClick: () => applyActionRequiredFilter(persistedFilterActionRequired === SyncStatus.PendingReview ? '' : SyncStatus.PendingReview),
            },
            {
              key: SyncStatus.ThresholdExceeded,
              text: strings.JobsList.JobsListFilter.filters.actionRequired.options.thresholdExceeded,
              onRenderIcon: renderCheckboxIcon(persistedFilterActionRequired === SyncStatus.ThresholdExceeded),
              onClick: () => applyActionRequiredFilter(persistedFilterActionRequired === SyncStatus.ThresholdExceeded ? '' : SyncStatus.ThresholdExceeded),
            },
            {
              key: SyncStatus.MembershipDataNotFound,
              text: strings.JobsList.JobsListFilter.filters.actionRequired.options.membershipDataNotFound,
              onRenderIcon: renderCheckboxIcon(persistedFilterActionRequired === SyncStatus.MembershipDataNotFound),
              onClick: () => applyActionRequiredFilter(persistedFilterActionRequired === SyncStatus.MembershipDataNotFound ? '' : SyncStatus.MembershipDataNotFound),
            },
            {
              key: SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup,
              text: strings.JobsList.JobsListFilter.filters.actionRequired.options.guestUsersCannotBeAddedToUnifiedGroup,
              onRenderIcon: renderCheckboxIcon(persistedFilterActionRequired === SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup),
              onClick: () => applyActionRequiredFilter(persistedFilterActionRequired === SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup ? '' : SyncStatus.GuestUsersCannotBeAddedToUnifiedGroup),
            },
          ]}
          directionalHint={DirectionalHint.bottomLeftEdge}
        />
      )}
      {columnMenuTarget && columnMenuColumn && dropdownSortColumns.includes(columnMenuColumn.key) && (
        <ContextualMenu
          target={columnMenuTarget}
          onDismiss={() => { setColumnMenuTarget(null); setColumnMenuColumn(null); }}
          styles={{ root: { fontSize: 12 }, subComponentStyles: { menuItem: { root: { fontSize: 12 } }, callout: {} } }}
          items={[
            {
              key: 'sortAsc',
              text: isDateColumn(columnMenuColumn.key) ? strings.JobsList.JobsListFilter.columnMenu.sortOlderToNewer : strings.JobsList.JobsListFilter.columnMenu.sortAtoZ,
              iconProps: { iconName: 'SortUp', ...checkboxIconStyle },
              onClick: () => applyColumnSort(columnMenuColumn, false),
            },
            {
              key: 'sortDesc',
              text: isDateColumn(columnMenuColumn.key) ? strings.JobsList.JobsListFilter.columnMenu.sortNewerToOlder : strings.JobsList.JobsListFilter.columnMenu.sortZtoA,
              iconProps: { iconName: 'SortDown', ...checkboxIconStyle },
              onClick: () => applyColumnSort(columnMenuColumn, true),
            },
          ]}
          directionalHint={DirectionalHint.bottomLeftEdge}
        />
      )}
    </div>
  );
};
