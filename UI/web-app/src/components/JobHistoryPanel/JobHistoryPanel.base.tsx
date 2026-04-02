// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    classNamesFunction,
    IProcessedStyleSet,
    DetailsList,
    DetailsRow,
    IDetailsRowProps,
    Panel,
    PanelType,
    Pivot,
    PivotItem,
    IColumn,
    Link,
    Modal,
    IconButton,
    Icon,
    Label,
    useTheme,
    TextField,
    MessageBar,
    MessageBarType,
    Dropdown,
    IDropdownOption,
    NormalPeoplePicker,
    Spinner,
    SpinnerSize,
} from '@fluentui/react';
import { IPersonaProps } from '@fluentui/react/lib/Persona';
import {
    IJobHistoryPanelProps, IJobHistoryPanelStyleProps, IJobHistoryPanelStyles,
} from './JobHistoryPanel.types';
import { useStrings } from '../../store/hooks';
import { useDispatch, useSelector } from 'react-redux';
import { AppDispatch } from '../../store';
import { useEffect, useRef, useState, useMemo } from 'react';
import { fetchJobChanges, fetchSyncJobHistory, downloadMembershipChanges, searchSyncHistoryByUser, fetchThresholdNotification } from '../../store/jobDetails.api';
import { selectSelectedJobChanges, selectSelectedJobDetails } from '../../store/jobs.slice';
import { SyncJobChange } from '../../models/SyncJobChange';
import { SyncJobChangeReason } from '../../models/SyncJobChangeReason';
import { SyncJobHistory } from '../../models/SyncJobHistory';
import { SyncHistorySearchProgressUpdate } from '../../models/SyncHistorySearchProgressUpdate';
import { ThresholdNotificationData } from '../../models/ThresholdNotificationData';
import { selectIsJobTenantReader, selectIsJobTenantWriter, selectIsSubmissionReviewer } from '../../store/roles.slice';
import { renderMultilineHeader } from '../../utils/stringUtils';
import { getStatusDisplayText } from '../../utils/jobUtils';
import { RunHistoryStatus } from '../../models/Status';
import { format } from 'react-string-format';
import { getPeoplePickerSuggestions } from '../../store/jobs.api';
import { SignalRSyncHistorySearchService } from '../../services/signalR/SignalRSyncHistorySearchService';
import { TakeActionModal } from '../TakeActionModal';

const getClassNames = classNamesFunction<
    IJobHistoryPanelStyleProps,
    IJobHistoryPanelStyles
>();


export const JobHistoryPanelBase: React.FunctionComponent<IJobHistoryPanelProps> = (
    props: IJobHistoryPanelProps
) => {
    const { className, styles, isOpen, dismissPanel, jobId } = props;
    const strings = useStrings();
    const theme = useTheme();
    const dispatch = useDispatch<AppDispatch>();
    const selectedJob = useSelector(selectSelectedJobDetails);

    const classNames: IProcessedStyleSet<IJobHistoryPanelStyles> = getClassNames(styles, { className, theme });

    const [downloadError, setDownloadError] = useState<string | null>(null);
    const [downloadingRunIds, setDownloadingRunIds] = useState<Set<string>>(new Set());
    const [sortedColumn, setSortedColumn] = useState<string>('endTime');
    const [isSortedDescending, setIsSortedDescending] = useState<boolean>(true);
    const [statusFilter, setStatusFilter] = useState<string>('all');
    const [selectedUser, setSelectedUser] = useState<IPersonaProps[]>([]);
    const [matchingRunIds, setMatchingRunIds] = useState<Set<string> | null>(null);
    const [isUserSearchLoading, setIsUserSearchLoading] = useState(false);
    const [userSearchError, setUserSearchError] = useState<string | null>(null);
    const [userSearchInfo, setUserSearchInfo] = useState<string | null>(null);
    const [showProgressUnavailableMessage, setShowProgressUnavailableMessage] = useState(false);
    const [searchProgressText, setSearchProgressText] = useState<string | null>(null);
    const [expandedRunIds, setExpandedRunIds] = useState<Set<string>>(new Set());

    const toggleRowExpand = (runId: string) => {
        setExpandedRunIds(prev => {
            const next = new Set(prev);
            if (next.has(runId)) {
                next.delete(runId);
            } else {
                next.add(runId);
            }
            return next;
        });
    };

    const syncHistorySearchSignalRServiceRef = useRef<SignalRSyncHistorySearchService>(new SignalRSyncHistorySearchService());
    const activeSearchRequestIdRef = useRef<string | null>(null);
    const isSignalRProgressDisabledRef = useRef(false);

    const buildRequestId = (): string => {
        if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
            return crypto.randomUUID();
        }

        return `${Date.now()}-${Math.random().toString(36).slice(2)}`;
    };

    const updateProgressText = (progress: SyncHistorySearchProgressUpdate) => {
        setSearchProgressText(`${strings.JobDetails.Panel.searchUserLoading} (${progress.processedRuns}/${progress.totalRuns})`);
    };

    useEffect(() => {
        const signalRService = syncHistorySearchSignalRServiceRef.current;
        signalRService.onProgress = (update: SyncHistorySearchProgressUpdate) => {
            if (!activeSearchRequestIdRef.current || update.requestId !== activeSearchRequestIdRef.current) {
                return;
            }

            updateProgressText(update);
        };

        return () => {
            signalRService.onProgress = null;
            signalRService.stopConnection();
        };
    }, [strings.JobDetails.Panel.searchUserLoading]);

    useEffect(() => {
        if (!showProgressUnavailableMessage || !isUserSearchLoading) {
            return;
        }

        const timeoutId = window.setTimeout(() => {
            setShowProgressUnavailableMessage(false);
        }, 5000);

        return () => {
            window.clearTimeout(timeoutId);
        };
    }, [showProgressUnavailableMessage, isUserSearchLoading]);

    useEffect(() => {
        setDownloadError(null);
        setStatusFilter('all');
        setSelectedUser([]);
        setMatchingRunIds(null);
        setIsUserSearchLoading(false);
        setUserSearchError(null);
        setUserSearchInfo(null);
        setShowProgressUnavailableMessage(false);
        setSearchProgressText(null);
        activeSearchRequestIdRef.current = null;
    }, [isOpen]);

    const getSelectedUserObjectId = (): string | null => {
        if (selectedUser.length === 0) return null;
        const persona = selectedUser[0];
        if (typeof persona.id === 'string' && persona.id.trim() !== '') {
            return persona.id;
        }

        if (typeof persona.key === 'string' && persona.key.trim() !== '') {
            return persona.key;
        }

        return null;
    };

    const removeDuplicates = (personas: IPersonaProps[], possibleDupes: IPersonaProps[]) => {
        return personas.filter(persona => !possibleDupes.some(item => item.id === persona.id));
    };

    const getPickerSuggestions = async (
        filterText: string,
        currentPersonas: IPersonaProps[] | undefined
    ): Promise<IPersonaProps[]> => {
        if (!filterText || filterText.trim() === '') {
            return [];
        }

        const users = await dispatch(getPeoplePickerSuggestions(filterText)).unwrap();

        const personas = users.map((user) => {
            return {
                key: user.id,
                id: user.id,
                text: user.text,
                secondaryText: user.secondaryText,
            };
        });

        return removeDuplicates(personas, currentPersonas || []);
    };

    const searchHistoryForUser = async (userObjectId: string) => {
        setUserSearchError(null);
        setUserSearchInfo(null);
        setShowProgressUnavailableMessage(false);
        setIsUserSearchLoading(true);
        setSearchProgressText(strings.JobDetails.Panel.searchUserLoading);

        const requestId = buildRequestId();
        const signalRService = syncHistorySearchSignalRServiceRef.current;
        let signalRSubscribedRequestId: string | null = null;
        let useSignalRProgress = false;

        try {
            if (!isSignalRProgressDisabledRef.current) {
                try {
                    await signalRService.startConnection();
                    if (activeSearchRequestIdRef.current) {
                        await signalRService.unsubscribe(activeSearchRequestIdRef.current);
                    }
                    activeSearchRequestIdRef.current = requestId;
                    await signalRService.subscribe(requestId);
                    signalRSubscribedRequestId = requestId;
                    useSignalRProgress = true;
                } catch {
                    isSignalRProgressDisabledRef.current = true;
                    activeSearchRequestIdRef.current = null;
                    setShowProgressUnavailableMessage(true);
                }
            }

            const result = await dispatch(searchSyncHistoryByUser({
                syncJobId: jobId,
                userObjectId,
                requestId: useSignalRProgress ? requestId : undefined,
            })).unwrap();
            setMatchingRunIds(new Set(result.matchingRunIds));

            if (result.matchingRunIds.length === 0 && result.checkedCurrentGroupMembership) {
                if (result.userInCurrentGroup) {
                    setUserSearchInfo(strings.JobDetails.Panel.userAddedPriorToHistoryMessage);
                } else {
                    setUserSearchInfo(strings.JobDetails.Panel.userNeverInGroupOrRemovedPriorToHistoryMessage);
                }
            }
        } catch {
            setMatchingRunIds(new Set());
            setUserSearchError(strings.JobDetails.Panel.searchUserError);
        } finally {
            setIsUserSearchLoading(false);
            setSearchProgressText(null);
            if (signalRSubscribedRequestId) {
                await signalRService.unsubscribe(signalRSubscribedRequestId);
            }
            activeSearchRequestIdRef.current = null;
        }
    };

    const onSelectedUserChanged = (items?: IPersonaProps[]) => {
        const users = items ?? [];
        setSelectedUser(users);
        setUserSearchError(null);
        setUserSearchInfo(null);
        setShowProgressUnavailableMessage(false);

        if (users.length === 0) {
            setMatchingRunIds(null);
            setIsUserSearchLoading(false);
            setSearchProgressText(null);
            activeSearchRequestIdRef.current = null;
            return;
        }

        const selectedObjectId =
            typeof users[0].id === 'string' && users[0].id.trim() !== ''
                ? users[0].id
                : typeof users[0].key === 'string' && users[0].key.trim() !== ''
                    ? users[0].key
                    : null;

        if (!selectedObjectId) {
            setMatchingRunIds(new Set());
            setUserSearchError(strings.JobDetails.Panel.searchUserError);
            return;
        }

        searchHistoryForUser(selectedObjectId);
    };

    const handleDownload = async (runId: string) => {
        if (downloadingRunIds.has(runId)) return;
        setDownloadError(null);
        setDownloadingRunIds(prev => new Set(prev).add(runId));
        try {
            await dispatch(downloadMembershipChanges({ syncJobId: jobId, runId, targetGroupId: selectedJob?.targetGroupId ?? '' })).unwrap();
        } catch {
            setDownloadError(strings.JobDetails.Panel.downloadError);
        } finally {
            setDownloadingRunIds(prev => {
                const next = new Set(prev);
                next.delete(runId);
                return next;
            });
        }
    };

    const getChangeTypeColorClass = (changeReason: string): string => {
        switch (changeReason) {
            case SyncJobChangeReason.SubmissionRejected:
                return classNames.changeTypeRejected;
            case SyncJobChangeReason.SubmissionApproved:
            case SyncJobChangeReason.OnboardingAutoApproved:
                return classNames.changeTypeApproved;
            case SyncJobChangeReason.Onboarding:
            case SyncJobChangeReason.Update:
            case SyncJobChangeReason.StatusUpdate:
            case SyncJobChangeReason.IgnoreThresholdOnce:
                return classNames.changeTypeUpdate;
            case SyncJobChangeReason.GroupSettings:
                return classNames.changeTypeGroupSettings;
            default:
                return classNames.changeTypeDefault;
        }
    };

    const getChangeReasonText = (changeReason: string): string => {
        switch (changeReason) {
            case SyncJobChangeReason.Onboarding:
                return strings.JobDetails.Panel.onboardingRequest;
            case SyncJobChangeReason.OnboardingAutoApproved:
                return strings.JobDetails.Panel.onboardingAutoApproved;
            case SyncJobChangeReason.StatusUpdate:
                return strings.JobDetails.Panel.statusUpdate;
            case SyncJobChangeReason.Update:
                return strings.JobDetails.Panel.update;
            case SyncJobChangeReason.SubmissionApproved:
                return strings.JobDetails.Panel.submissionApproved;
            case SyncJobChangeReason.SubmissionRejected:
                return strings.JobDetails.Panel.submissionRejected;
            case SyncJobChangeReason.GroupSettings:
                return strings.JobDetails.Panel.groupSettings;
            case SyncJobChangeReason.IgnoreThresholdOnce:
                return strings.JobDetails.Panel.ignoreThresholdOnce;
            default:
                return changeReason;
        }
    };

    const columns: IColumn[] = [
        {
            key: 'changeTime',
            name: strings.JobDetails.Panel.changeTimeColumnLabel,
            fieldName: 'changeTime',
            minWidth: 100,
            maxWidth: 150,
            isResizable: true,
            isMultiline: true,
            onRender: (item: SyncJobChange) => {
                const utcDate = item.changeTime.endsWith('Z') ? item.changeTime : `${item.changeTime}Z`;
                const utcDateObj = new Date(utcDate);
                const localDate = utcDateObj.toLocaleDateString();
                const localTime = utcDateObj.toLocaleTimeString();
                return (
                    <div className={classNames.dateTimeContainer}>
                        <div className={classNames.dateText}>{localDate}</div>
                        <div className={classNames.timeText}>{localTime}</div>
                    </div>
                );
            }
        },
        {
            key: 'changedByDisplayName',
            name: strings.JobDetails.Panel.changedByColumnLabel,
            fieldName: 'changedByDisplayName',
            minWidth: 120,
            maxWidth: 200,
            isResizable: true,
            isMultiline: true,
        },
        {
            key: 'changeReason',
            name: strings.JobDetails.Panel.changeReasonColumnLabel,
            fieldName: 'changeReason',
            minWidth: 150,
            maxWidth: 250,
            isResizable: true,
            isMultiline: true,
            onRender: (item: SyncJobChange) => {
                const colorClass = getChangeTypeColorClass(item.changeReason);
                const text = getChangeReasonText(item.changeReason);
                return (
                    <div className={classNames.changeReasonContainer}>
                        <span aria-hidden="true" className={`${classNames.changeTypeIndicator} ${colorClass}`} />
                        <span>{text}</span>
                    </div>
                );
            }
        },
        {
            key: 'businessJustification',
            name: strings.JobDetails.Panel.businessJustification,
            fieldName: 'businessJustification',
            minWidth: 150,
            maxWidth: 300,
            isResizable: true,
            isMultiline: true,
            onRender: (item: SyncJobChange) => {
                return <span>{item.businessJustification}</span>;
            }
        },
        {
            key: 'changeDetails',
            name: strings.JobDetails.Panel.changeDetailsColumnLabel,
            fieldName: 'changeDetails',
            minWidth: 100,
            maxWidth: 150,
            isResizable: true,
            isMultiline: true,
            onRender: (item: SyncJobChange) => {
                return <Link onClick={() => handleViewDetails(item.changeDetails)}>
                    {strings.JobDetails.Panel.viewDetails}
                </Link>;
            }
        }
    ];

    const handleColumnHeaderClick = (event?: React.MouseEvent<HTMLElement>, column?: IColumn): void => {
        if (!column) return;
        
        const newIsSortedDescending = sortedColumn === column.key ? !isSortedDescending : true;
        setSortedColumn(column.key);
        setIsSortedDescending(newIsSortedDescending);
    };

    const isJobTenantReader = useSelector(selectIsJobTenantReader);
    const isJobTenantWriter = useSelector(selectIsJobTenantWriter);
    const isSubmissionReviewer = useSelector(selectIsSubmissionReviewer);
    const showDownloadColumn = isJobTenantReader || isJobTenantWriter || isSubmissionReviewer;

    const syncHistoryColumns: IColumn[] = [
        {
            key: 'endTime',
            name: strings.JobDetails.Panel.endTimeColumnLabel,
            fieldName: 'endTime',
            minWidth: 100,
            maxWidth: 150,
            isResizable: true,
            isMultiline: true,
            isSorted: sortedColumn === 'endTime',
            isSortedDescending: isSortedDescending,
            onColumnClick: handleColumnHeaderClick,
            onRender: (item: SyncJobHistory) => {
                if (!item.endTime) return <span>-</span>;
                const utcDate = item.endTime.endsWith('Z') ? item.endTime : `${item.endTime}Z`;
                const utcDateObj = new Date(utcDate);
                const localDate = utcDateObj.toLocaleDateString();
                const localTime = utcDateObj.toLocaleTimeString();
                return (
                    <div className={classNames.dateTimeContainer}>
                        <div className={classNames.dateText}>{localDate}</div>
                        <div className={classNames.timeText}>{localTime}</div>
                    </div>
                );
            }
        },
        {
            key: 'status',
            name: strings.JobDetails.Panel.statusColumnLabel,
            fieldName: 'status',
            minWidth: 200,
            maxWidth: 300,
            isResizable: true,
            onRender: (item: SyncJobHistory) => {
                const isThresholdExceeded = item.status === RunHistoryStatus.ThresholdExceeded;
                return (
                    <div className={classNames.statusCellContainer}>
                        <span className={isThresholdExceeded ? classNames.statusCellThresholdExceeded : undefined}>
                            {getStatusDisplayText(item.status)}
                        </span>
                        {isThresholdExceeded && (
                            <Link onClick={() => handleTakeAction(item)}>{strings.JobDetails.Panel.takeAction}</Link>
                        )}
                    </div>
                );
            }
        },
        {
            key: 'beforeSyncUserCount',
            name: strings.JobDetails.Panel.beforeSyncUserCountColumnLabel,
            fieldName: 'beforeSyncUserCount',
            minWidth: 80,
            maxWidth: 120,
            isResizable: true,
            isMultiline: true,
            isSorted: sortedColumn === 'beforeSyncUserCount',
            isSortedDescending: isSortedDescending,
            onColumnClick: handleColumnHeaderClick,
            onRenderHeader: () => renderMultilineHeader(strings.JobDetails.Panel.beforeSyncUserCountColumnLabel),
            onRender: (item: SyncJobHistory) => {
                return <span>{item.beforeSyncUserCount ?? '-'}</span>;
            }
        },
        {
            key: 'usersAdded',
            name: strings.JobDetails.Panel.usersAddedColumnLabel,
            fieldName: 'usersAdded',
            minWidth: 80,
            maxWidth: 120,
            isResizable: true,
            isSorted: sortedColumn === 'usersAdded',
            isSortedDescending: isSortedDescending,
            onColumnClick: handleColumnHeaderClick,
            onRender: (item: SyncJobHistory) => {
                return <span>{item.usersAdded ?? '-'}</span>;
            }
        },
        {
            key: 'usersRemoved',
            name: strings.JobDetails.Panel.usersRemovedColumnLabel,
            fieldName: 'usersRemoved',
            minWidth: 80,
            maxWidth: 120,
            isResizable: true,
            isSorted: sortedColumn === 'usersRemoved',
            isSortedDescending: isSortedDescending,
            onColumnClick: handleColumnHeaderClick,
            onRender: (item: SyncJobHistory) => {
                return <span>{item.usersRemoved ?? '-'}</span>;
            }
        },
        {
            key: 'afterSyncUserCount',
            name: strings.JobDetails.Panel.afterSyncUserCountColumnLabel,
            fieldName: 'afterSyncUserCount',
            minWidth: 80,
            maxWidth: 120,
            isResizable: true,
            isMultiline: true,
            isSorted: sortedColumn === 'afterSyncUserCount',
            isSortedDescending: isSortedDescending,
            onColumnClick: handleColumnHeaderClick,
            onRender: (item: SyncJobHistory) => {
                return <span>{item.afterSyncUserCount ?? '-'}</span>;
            }
        },
        {
            key: 'thresholdViolations',
            name: strings.JobDetails.Panel.thresholdViolationsColumnLabel,
            fieldName: 'thresholdViolations',
            minWidth: 100,
            maxWidth: 150,
            isResizable: true,
            isSorted: sortedColumn === 'thresholdViolations',
            isSortedDescending: isSortedDescending,
            onColumnClick: handleColumnHeaderClick,
            onRender: (item: SyncJobHistory) => {
                return <span>{item.thresholdViolations ?? '-'}</span>;
            }
        },
        {
            key: 'expand',
            name: '',
            fieldName: '',
            minWidth: 40,
            maxWidth: 40,
            isResizable: false,
            onRender: (item: SyncJobHistory) => {
                const isExpanded = expandedRunIds.has(item.runId);
                return (
                    <IconButton
                        iconProps={{ iconName: isExpanded ? 'ChevronUp' : 'ChevronDown' }}
                        ariaLabel={isExpanded ? 'Collapse row' : 'Expand row'}
                        onClick={(e) => {
                            e.stopPropagation();
                            toggleRowExpand(item.runId);
                        }}
                    />
                );
            },
        },
        ...(showDownloadColumn ? [{
            key: 'download',
            name: strings.JobDetails.Panel.downloadColumnLabel,
            fieldName: 'download',
            minWidth: 80,
            maxWidth: 120,
            isResizable: true,
            onRender: (item: SyncJobHistory) => {
                const hasChanges = (item.usersAdded ?? 0) > 0 || (item.usersRemoved ?? 0) > 0;
                if (!hasChanges) return null;
                return (
                    <Link
                        onClick={() => handleDownload(item.runId)}
                        disabled={downloadingRunIds.has(item.runId)}
                        aria-label={format(strings.JobDetails.Panel.downloadAriaLabel, item.runId)}
                    >
                        {downloadingRunIds.has(item.runId) ? strings.JobDetails.Panel.downloadingText : strings.JobDetails.Panel.downloadLinkText}
                    </Link>
                );
            }
        }] : [])
    ];

    const [detailsListItems, setDetailsListItems] = useState<SyncJobChange[]>([]);
    const [syncHistoryItems, setSyncHistoryItems] = useState<SyncJobHistory[]>([]);
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [modalContent, setModalContent] = useState('');
    const [takeActionItem, setTakeActionItem] = useState<SyncJobHistory | null>(null);
    const [thresholdData, setThresholdData] = useState<ThresholdNotificationData | null>(null);
    const [isThresholdDataLoading, setIsThresholdDataLoading] = useState(false);

    const getUtcTimestampMillis = (dateTime?: string | null): number => {
        if (!dateTime) return 0;
        const utcDateTime = dateTime.endsWith('Z') ? dateTime : `${dateTime}Z`;
        const millis = new Date(utcDateTime).getTime();
        return Number.isNaN(millis) ? 0 : millis;
    };

    const sortedSyncHistoryItems = useMemo(() => {
        const items = [...syncHistoryItems];
        
        items.sort((a: SyncJobHistory, b: SyncJobHistory) => {
            let aValue: number | string;
            let bValue: number | string;

            switch (sortedColumn) {
                case 'endTime':
                    aValue = getUtcTimestampMillis(a.endTime);
                    bValue = getUtcTimestampMillis(b.endTime);
                    break;
                case 'beforeSyncUserCount':
                    aValue = a.beforeSyncUserCount ?? 0;
                    bValue = b.beforeSyncUserCount ?? 0;
                    break;
                case 'usersAdded':
                    aValue = a.usersAdded ?? 0;
                    bValue = b.usersAdded ?? 0;
                    break;
                case 'usersRemoved':
                    aValue = a.usersRemoved ?? 0;
                    bValue = b.usersRemoved ?? 0;
                    break;
                case 'afterSyncUserCount':
                    aValue = a.afterSyncUserCount ?? 0;
                    bValue = b.afterSyncUserCount ?? 0;
                    break;
                case 'thresholdViolations':
                    aValue = a.thresholdViolations ?? 0;
                    bValue = b.thresholdViolations ?? 0;
                    break;
                default:
                    return 0;
            }

            if (aValue < bValue) {
                return isSortedDescending ? 1 : -1;
            }
            if (aValue > bValue) {
                return isSortedDescending ? -1 : 1;
            }
            return 0;
        });

        return items;
    }, [syncHistoryItems, sortedColumn, isSortedDescending]);

    const jobChanges: SyncJobChange[] | undefined = useSelector(selectSelectedJobChanges);
    const showSyncTab = isJobTenantReader || isJobTenantWriter;

    const statusOptions: IDropdownOption[] = [
        { key: 'all', text: strings.JobDetails.Panel.statusFilterAllOption },
        ...Array.from(new Set(syncHistoryItems.map((item) => item.status))).map((status) => ({
            key: status,
            text: getStatusDisplayText(status),
        })),
    ];

    const syncHistoryItemsFilteredByStatus = statusFilter === 'all'
        ? sortedSyncHistoryItems
        : sortedSyncHistoryItems.filter((item) => item.status === statusFilter);

    const filteredSyncHistoryItems = matchingRunIds === null
        ? syncHistoryItemsFilteredByStatus
        : syncHistoryItemsFilteredByStatus.filter((item) => matchingRunIds.has(item.runId));

    useEffect(() => {
        if (isOpen) {
            dispatch(fetchJobChanges({ syncJobId: jobId }));
            if (showSyncTab) {
                dispatch(fetchSyncJobHistory(jobId))
                    .unwrap()
                    .then((history) => {
                        setSyncHistoryItems(history);
                        const selectedObjectId = getSelectedUserObjectId();
                        if (selectedObjectId) {
                            searchHistoryForUser(selectedObjectId);
                        }
                    })
                    .catch((error) => {
                        console.error('Failed to fetch sync job history:', error);
                    });
            }
        }
    }, [isOpen, dispatch, jobId, showSyncTab]);

    useEffect(() => {
        if (jobChanges) {
            setDetailsListItems(jobChanges);
        }
    }, [jobChanges]);

    const handleViewDetails = (details: string) => {
        setModalContent(details);
        setIsModalOpen(true);
    };

    const handleCloseModal = () => {
        setIsModalOpen(false);
        setModalContent('');
    };

    const handleTakeAction = async (item: SyncJobHistory) => {
        setTakeActionItem(item);
        setThresholdData(null);
        setIsThresholdDataLoading(true);
        try {
            const data = await dispatch(fetchThresholdNotification(jobId)).unwrap();
            setThresholdData(data);
        } catch {
            setThresholdData(null);
        } finally {
            setIsThresholdDataLoading(false);
        }
    };

    const handleCloseTakeAction = () => {
        setTakeActionItem(null);
        setThresholdData(null);
        setIsThresholdDataLoading(false);
    };

    const parseNestedJson = (obj: any) => {
        for (const key in obj) {
            if (typeof obj[key] === 'string') {
                try {
                    obj[key] = JSON.parse(obj[key]);
                    parseNestedJson(obj[key]); // Recursively parse nested JSON strings
                } catch (e) {
                    // Not a JSON string, leave it as is
                }
            }
        }
        return obj;
    };

    let formattedDetails;
    try {
        const parsedDetails = JSON.parse(modalContent);
        const cleanedDetails = parseNestedJson(parsedDetails);
        formattedDetails = JSON.stringify(cleanedDetails, null, 2);
    } catch (e) {
        formattedDetails = modalContent;
    }

    const onRenderSyncHistoryRow = (rowProps?: IDetailsRowProps): JSX.Element => {
        if (!rowProps) return <></>;
        const item = rowProps.item as SyncJobHistory;
        const isExpanded = expandedRunIds.has(item.runId);
        return (
            <>
                <DetailsRow
                    {...rowProps}
                    styles={isExpanded ? { root: { borderBottom: 'none' } } : undefined}
                />
                {isExpanded && (
                    <div style={{ padding: '4px 12px 8px 12px', backgroundColor: 'inherit', borderBottom: '1px solid #edebe9' }}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                            <strong style={{ fontSize: '14px' }}>{strings.JobDetails.Panel.runIdColumnLabel}:</strong>
                            <span style={{ fontSize: '12px' }}>{item.runId}</span>
                        </div>
                    </div>
                )}
            </>
        );
    };

    return (
        <Panel
            type={PanelType.medium}
            isLightDismiss
            isOpen={isOpen}
            onDismiss={dismissPanel}
            headerText={strings.JobDetails.Panel.history}
            closeButtonAriaLabel={strings.close}
        >
            <Pivot>
                <PivotItem
                    headerText={strings.JobDetails.Panel.configurationPivotHeader}
                    headerButtonProps={{
                        'data-order': 1,
                        'data-title': strings.JobDetails.Panel.configurationPivotHeader
                    }}
                >
                    <DetailsList
                        setKey="set"
                        columns={columns}
                        items={detailsListItems}
                        selectionMode={0}
                    />
                </PivotItem>
                {showSyncTab && (
                    <PivotItem
                        headerText={strings.JobDetails.Panel.syncPivotHeader}
                        headerButtonProps={{
                            'data-order': 2,
                            'data-title': strings.JobDetails.Panel.syncPivotHeader
                        }}
                    >
                        {downloadError && (
                            <MessageBar
                                messageBarType={MessageBarType.error}
                                onDismiss={() => setDownloadError(null)}
                            >
                                {downloadError}
                            </MessageBar>
                        )}
                        <div className={classNames.syncFiltersContainer}>
                            <Dropdown
                                className={classNames.statusFilter}
                                label={strings.JobDetails.Panel.statusFilterLabel}
                                selectedKey={statusFilter}
                                options={statusOptions}
                                onChange={(_, option) => setStatusFilter((option?.key as string) ?? 'all')}
                            />
                            <div className={classNames.userSearchField}>
                                <Label className={classNames.userSearchLabel}>{strings.JobDetails.Panel.searchUserLabel}</Label>
                                <NormalPeoplePicker
                                        aria-label={strings.JobDetails.Panel.searchUserLabel}
                                        className={classNames.userSearchPicker}
                                        onResolveSuggestions={getPickerSuggestions}
                                        onChange={onSelectedUserChanged}
                                        selectedItems={selectedUser}
                                        itemLimit={1}
                                        resolveDelay={300}
                                        inputProps={{ placeholder: strings.JobDetails.Panel.searchUserPlaceholder }}
                                        pickerSuggestionsProps={{ noResultsFoundText: strings.JobDetails.Panel.searchUserNoResults }}
                                    />
                            </div>
                        </div>
                        {isUserSearchLoading && (
                            <Spinner label={searchProgressText ?? strings.JobDetails.Panel.searchUserLoading} size={SpinnerSize.small} />
                        )}
                        {showProgressUnavailableMessage && isUserSearchLoading && (
                            <MessageBar messageBarType={MessageBarType.info}>
                                {strings.JobDetails.Panel.searchUserProgressUnavailableMessage}
                            </MessageBar>
                        )}
                        {userSearchError && (
                            <MessageBar
                                messageBarType={MessageBarType.error}
                                onDismiss={() => setUserSearchError(null)}
                            >
                                {userSearchError}
                            </MessageBar>
                        )}
                        {userSearchInfo && !isUserSearchLoading && (
                            <MessageBar messageBarType={MessageBarType.info}>
                                {userSearchInfo}
                            </MessageBar>
                        )}
                        <DetailsList
                            setKey="syncHistorySet"
                            columns={syncHistoryColumns}
                            items={filteredSyncHistoryItems}
                            selectionMode={0}
                            onRenderRow={onRenderSyncHistoryRow}
                        />
                    </PivotItem>
                )}
            </Pivot>
            <Modal
                isOpen={isModalOpen}
                onDismiss={handleCloseModal}
                className={classNames.container}
            >
                <div className={classNames.header}>
                    <IconButton
                        iconProps={{ iconName: 'Cancel' }}
                        ariaLabel={strings.close}
                        onClick={handleCloseModal}
                    />
                </div>
                <div>
                    <TextField
                        value={formattedDetails}
                        readOnly
                        multiline
                        resizable={true}
                    />
                </div>
            </Modal>
            <TakeActionModal
                isOpen={takeActionItem !== null}
                isLoading={isThresholdDataLoading}
                onDismiss={handleCloseTakeAction}
                groupName={selectedJob?.targetGroupName ?? ''}
                usersToAdd={thresholdData?.changeQuantityForAdditions ?? 0}
                increasePercentage={thresholdData?.changePercentageForAdditions ?? 0}
                thresholdPercentage={thresholdData?.thresholdPercentageForAdditions ?? 0}
                onApplyChanges={() => {}}
                onEditRules={() => {}}
                onEditThreshold={() => {}}
                onPauseSync={() => {}}
            />
        </Panel>
    )
};
