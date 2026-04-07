// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    classNamesFunction,
    IProcessedStyleSet,
    DetailsList,
    DetailsListLayoutMode,
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
    useTheme,
    TextField,
    MessageBar,
    MessageBarType,
    Dropdown,
    IDropdownOption,
    Label,
    NormalPeoplePicker,
    Spinner,
    SpinnerSize,
    DirectionalHint,
} from '@fluentui/react';
import { IPersonaProps } from '@fluentui/react/lib/Persona';
import {
    IJobHistoryPanelProps, IJobHistoryPanelStyleProps, IJobHistoryPanelStyles,
} from './JobHistoryPanel.types';
import { useStrings } from '../../store/hooks';
import { useDispatch, useSelector } from 'react-redux';
import { AppDispatch } from '../../store';
import { useEffect, useMemo, useRef, useState } from 'react';
import { downloadMembershipChanges, fetchJobChanges, fetchSyncJobHistory, fetchThresholdNotification, resolveNotification, searchSyncHistoryByUser } from '../../store/jobDetails.api';
import { selectSelectedJobChanges, selectSelectedJobDetails, setSelectedJobEnabled } from '../../store/jobs.slice';
import { SyncJobChange } from '../../models/SyncJobChange';
import { SyncJobChangeReason } from '../../models/SyncJobChangeReason';
import { SyncJobHistory } from '../../models/SyncJobHistory';
import { SyncHistorySearchProgressUpdate } from '../../models/SyncHistorySearchProgressUpdate';
import { ThresholdNotificationData } from '../../models/ThresholdNotificationData';
import { selectIsJobTenantReader, selectIsJobTenantWriter } from '../../store/roles.slice';
import { renderMultilineHeader } from '../../utils/stringUtils';
import { getStatusDisplayText } from '../../utils/jobUtils';
import { RunHistoryStatus } from '../../models/Status';
import { format } from 'react-string-format';
import { ThresholdExceededActionDialog } from '../ThresholdExceededActionDialog';
import { getPeoplePickerSuggestions } from '../../store/jobs.api';
import { SignalRSyncHistorySearchService } from '../../services/signalR/SignalRSyncHistorySearchService';

const getClassNames = classNamesFunction<
    IJobHistoryPanelStyleProps,
    IJobHistoryPanelStyles
>();

type CombinedHistoryListItem = {
    id: string;
    eventType: 'sync' | 'configuration';
    time: string | null;
    statusText: string;
    beforeSyncUserCount: number | null;
    usersAdded: number | null;
    usersRemoved: number | null;
    afterSyncUserCount: number | null;
    syncHistory?: SyncJobHistory;
    jobChange?: SyncJobChange;
};

type SyncSortKey =
    | 'time'
    | 'eventType'
    | 'status'
    | 'beforeSyncUserCount'
    | 'usersAdded'
    | 'usersRemoved'
    | 'afterSyncUserCount';

const syncPageSizeOptions: IDropdownOption[] = [10, 20, 30, 40, 50].map((value) => ({
    key: value,
    text: value.toString(),
}));


export const JobHistoryPanelBase: React.FunctionComponent<IJobHistoryPanelProps> = (
    props: IJobHistoryPanelProps
) => {
    const { className, styles, isOpen, dismissPanel, jobId, onEditThreshold, onEditRules } = props;
    const strings = useStrings();
    const theme = useTheme();
    const dispatch = useDispatch<AppDispatch>();
    const jobChanges = useSelector(selectSelectedJobChanges) ?? [];
    const selectedJob = useSelector(selectSelectedJobDetails);
    const isJobTenantReader = useSelector(selectIsJobTenantReader);
    const isJobTenantWriter = useSelector(selectIsJobTenantWriter);
    const showSyncTab = isJobTenantReader || isJobTenantWriter;
    const canDownloadMembershipChanges = isJobTenantWriter;

    const classNames: IProcessedStyleSet<IJobHistoryPanelStyles> = getClassNames(styles, { className, theme });

    const [syncHistoryItems, setSyncHistoryItems] = useState<SyncJobHistory[]>([]);
    const [expandedSyncRowIds, setExpandedSyncRowIds] = useState<Set<string>>(new Set());
    const [downloadError, setDownloadError] = useState<string | null>(null);
    const [downloadingRunIds, setDownloadingRunIds] = useState<Set<string>>(new Set());
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [modalContent, setModalContent] = useState('');
    const [modalTitle, setModalTitle] = useState('');
    const [modalViewMode, setModalViewMode] = useState<'details' | 'query'>('details');
    const [syncSortKey, setSyncSortKey] = useState<SyncSortKey>('time');
    const [isSyncSortDescending, setIsSyncSortDescending] = useState(true);
    const [syncPageNumber, setSyncPageNumber] = useState(1);
    const [syncPageSize, setSyncPageSize] = useState(10);
    const [takeActionItem, setTakeActionItem] = useState<SyncJobHistory | null>(null);
    const [thresholdData, setThresholdData] = useState<ThresholdNotificationData | null>(null);
    const [isThresholdDataLoading, setIsThresholdDataLoading] = useState(false);
    const [syncPaused, setSyncPaused] = useState(false);
    const [changesApplied, setChangesApplied] = useState(false);
    const [resolveError, setResolveError] = useState<string | null>(null);
    const [resolvedRunIds, setResolvedRunIds] = useState<Set<string>>(new Set());
    const [selectedUser, setSelectedUser] = useState<IPersonaProps[]>([]);
    const [matchingRunIds, setMatchingRunIds] = useState<Set<string> | null>(null);
    const [isUserSearchLoading, setIsUserSearchLoading] = useState(false);
    const [userSearchError, setUserSearchError] = useState<string | null>(null);
    const [userSearchInfo, setUserSearchInfo] = useState<string | null>(null);
    const [showProgressUnavailableMessage, setShowProgressUnavailableMessage] = useState(false);
    const [searchProgressText, setSearchProgressText] = useState<string | null>(null);
    const [userPickerSuggestions, setUserPickerSuggestions] = useState<IPersonaProps[]>([]);

    const syncHistorySearchSignalRServiceRef = useRef<SignalRSyncHistorySearchService>(new SignalRSyncHistorySearchService());
    const activeSearchRequestIdRef = useRef<string | null>(null);
    const isSignalRProgressDisabledRef = useRef(false);
    const userPickerRef = useRef<any>(null);
    const ignoreNextEmptyUserInputRef = useRef(false);

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

    const getUtcTimestampMillis = (dateTime?: string | null): number => {
        if (!dateTime) return 0;

        const utcDateTime = dateTime.endsWith('Z') ? dateTime : `${dateTime}Z`;
        const millis = new Date(utcDateTime).getTime();
        return Number.isNaN(millis) ? 0 : millis;
    };

    const buildRequestId = (): string => {
        if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
            return crypto.randomUUID();
        }

        return `${Date.now()}-${Math.random().toString(36).slice(2)}`;
    };

    const updateProgressText = (progress: SyncHistorySearchProgressUpdate): void => {
        setSearchProgressText(`${strings.JobDetails.Panel.searchUserLoading} (${progress.processedRuns}/${progress.totalRuns})`);
    };

    const updateUserPickerPopup = (suggestions: IPersonaProps[]): void => {
        const picker = userPickerRef.current as {
            input?: { current?: { inputElement?: HTMLInputElement | null } | null };
            setState?: (state: {
                suggestionsVisible: boolean;
                suggestionsLoading: boolean;
                suggestionsExtendedLoading: boolean;
                isMostRecentlyUsedVisible: boolean;
            }) => void;
            updateSuggestions?: (nextSuggestions: IPersonaProps[]) => void;
        } | null;

        const inputElement = picker?.input?.current?.inputElement ?? null;
        const isInputFocused = Boolean(inputElement && document.activeElement === inputElement);
        const shouldShowSuggestions = isInputFocused && suggestions.length > 0 && Boolean(inputElement?.value?.trim());

        if (!picker?.setState) {
            return;
        }

        if (typeof picker.updateSuggestions === 'function') {
            picker.updateSuggestions(suggestions);
        }

        picker.setState({
            suggestionsVisible: shouldShowSuggestions,
            suggestionsLoading: false,
            suggestionsExtendedLoading: false,
            isMostRecentlyUsedVisible: false,
        });
    };

    const getSelectedUserObjectId = (users: IPersonaProps[] = selectedUser): string | null => {
        if (users.length === 0) {
            return null;
        }

        const persona = users[0];
        if (typeof persona.id === 'string' && persona.id.trim() !== '') {
            return persona.id;
        }

        if (typeof persona.key === 'string' && persona.key.trim() !== '') {
            return persona.key;
        }

        return null;
    };

    const removeDuplicatePersonas = (personas: IPersonaProps[], possibleDuplicates: IPersonaProps[]): IPersonaProps[] => {
        return personas.filter((persona) => !possibleDuplicates.some((item) => {
            const existingId = item.id ?? item.key;
            const personaId = persona.id ?? persona.key;
            return existingId === personaId;
        }));
    };

    const getPickerSuggestions = async (
        filterText: string,
        currentPersonas?: IPersonaProps[],
    ): Promise<IPersonaProps[]> => {
        const normalizedFilterText = typeof filterText === 'string' ? filterText : '';
        const trimmedFilter = normalizedFilterText.trim();

        if (!trimmedFilter) {
            return [];
        }

        return removeDuplicatePersonas(userPickerSuggestions, currentPersonas ?? []);
    };

    const renderUserSuggestion = (persona: IPersonaProps): JSX.Element => {
        const primaryText = typeof persona.text === 'string' && persona.text.trim() !== ''
            ? persona.text
            : typeof persona.secondaryText === 'string'
                ? persona.secondaryText
                : '';
        const secondaryText = typeof persona.secondaryText === 'string' && persona.secondaryText !== primaryText
            ? persona.secondaryText
            : '';

        return (
            <div className={classNames.userSuggestionRow}>
                <div className={classNames.userSuggestionPrimaryText}>{primaryText}</div>
                {secondaryText && (
                    <div className={classNames.userSuggestionSecondaryText}>{secondaryText}</div>
                )}
            </div>
        );
    };

    const clearUserSearch = (): void => {
        ignoreNextEmptyUserInputRef.current = false;
        setSelectedUser([]);
        setUserPickerSuggestions([]);
        setMatchingRunIds(null);
        setIsUserSearchLoading(false);
        setUserSearchError(null);
        setUserSearchInfo(null);
        setShowProgressUnavailableMessage(false);
        setSearchProgressText(null);
        activeSearchRequestIdRef.current = null;
        window.requestAnimationFrame(() => updateUserPickerPopup([]));
    };

    const handleUserSearchInputChange = (input: string): string => {
        const normalizedInput = typeof input === 'string' ? input : '';
        const trimmedInput = normalizedInput.trim();

        if (!trimmedInput) {
            if (ignoreNextEmptyUserInputRef.current) {
                ignoreNextEmptyUserInputRef.current = false;
                return normalizedInput;
            }

            clearUserSearch();
            return normalizedInput;
        }

        ignoreNextEmptyUserInputRef.current = false;

        dispatch(getPeoplePickerSuggestions(trimmedInput))
            .unwrap()
            .then((users) => {
                const personas = users.map((user) => ({
                    key: user.id,
                    id: user.id,
                    text: user.text,
                    secondaryText: user.secondaryText,
                }));

                setUserPickerSuggestions(personas);
                window.requestAnimationFrame(() => updateUserPickerPopup(personas));
            })
            .catch(() => {
                setUserPickerSuggestions([]);
                window.requestAnimationFrame(() => updateUserPickerPopup([]));
            });

        return normalizedInput;
    };

    const searchHistoryForUser = async (userObjectId: string): Promise<void> => {
        setMatchingRunIds(null);
        setSyncPageNumber(1);
        setUserSearchError(null);
        setUserSearchInfo(null);
        setShowProgressUnavailableMessage(false);
        setIsUserSearchLoading(true);
        setSearchProgressText(strings.JobDetails.Panel.searchUserLoading);

        const requestId = buildRequestId();
        const signalRService = syncHistorySearchSignalRServiceRef.current;
        let signalRSubscribedRequestId: string | null = null;
        let useSignalRProgress = false;

        activeSearchRequestIdRef.current = requestId;

        try {
            if (!isSignalRProgressDisabledRef.current) {
                try {
                    await signalRService.startConnection();
                    await signalRService.subscribe(requestId);
                    signalRSubscribedRequestId = requestId;
                    useSignalRProgress = true;
                } catch {
                    isSignalRProgressDisabledRef.current = true;
                    setShowProgressUnavailableMessage(true);
                }
            }

            const result = await dispatch(searchSyncHistoryByUser({
                syncJobId: jobId,
                userObjectId,
                requestId: useSignalRProgress ? requestId : undefined,
            })).unwrap();

            if (activeSearchRequestIdRef.current !== requestId) {
                return;
            }

            setMatchingRunIds(new Set(result.matchingRunIds));

            if (result.matchingRunIds.length === 0 && result.checkedCurrentGroupMembership) {
                if (result.userInCurrentGroup) {
                    setUserSearchInfo(strings.JobDetails.Panel.userAddedPriorToHistoryMessage);
                } else {
                    setUserSearchInfo(strings.JobDetails.Panel.userNeverInGroupOrRemovedPriorToHistoryMessage);
                }
            }
        } catch {
            if (activeSearchRequestIdRef.current !== requestId) {
                return;
            }

            setMatchingRunIds(new Set());
            setUserSearchError(strings.JobDetails.Panel.searchUserError);
        } finally {
            if (signalRSubscribedRequestId) {
                try {
                    await signalRService.unsubscribe(signalRSubscribedRequestId);
                } catch {
                    // Ignore SignalR unsubscribe failures.
                }
            }

            if (activeSearchRequestIdRef.current === requestId) {
                activeSearchRequestIdRef.current = null;
                setIsUserSearchLoading(false);
                setSearchProgressText(null);
            }
        }
    };

    const onSelectedUserChanged = (items?: IPersonaProps[]): void => {
        const users = items ?? [];
        ignoreNextEmptyUserInputRef.current = users.length > 0;

        setSelectedUser(users);
        setSyncPageNumber(1);
        setUserSearchError(null);
        setUserSearchInfo(null);
        setShowProgressUnavailableMessage(false);
        updateUserPickerPopup([]);

        if (users.length === 0) {
            clearUserSearch();
            return;
        }

        const selectedObjectId = getSelectedUserObjectId(users);

        if (!selectedObjectId) {
            ignoreNextEmptyUserInputRef.current = false;
            activeSearchRequestIdRef.current = null;
            setMatchingRunIds(new Set());
            setUserPickerSuggestions([]);
            setIsUserSearchLoading(false);
            setSearchProgressText(null);
            setUserSearchError(strings.JobDetails.Panel.searchUserError);
            return;
        }

        void searchHistoryForUser(selectedObjectId);
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

    const renderTimestamp = (dateTime: string | null): JSX.Element => {
        if (!dateTime) {
            return <span>{strings.JobDetails.Panel.emptyValuePlaceholder}</span>;
        }

        const utcDate = dateTime.endsWith('Z') ? dateTime : `${dateTime}Z`;
        const utcDateObj = new Date(utcDate);

        return (
            <div className={classNames.dateTimeContainer}>
                <div className={classNames.dateText}>{utcDateObj.toLocaleDateString()}</div>
                <div className={classNames.timeText}>{utcDateObj.toLocaleTimeString()}</div>
            </div>
        );
    };

    const renderCount = (value: number | null): JSX.Element => {
        return <span>{value ?? strings.JobDetails.Panel.emptyValuePlaceholder}</span>;
    };

    const canRenderDownloadLink = (item: CombinedHistoryListItem): boolean => {
        if (!canDownloadMembershipChanges || item.eventType !== 'sync' || !item.syncHistory) {
            return false;
        }

        return (item.usersAdded ?? 0) > 0 || (item.usersRemoved ?? 0) > 0;
    };

    const renderDownloadLink = (item: CombinedHistoryListItem): JSX.Element | null => {
        if (!canRenderDownloadLink(item) || !item.syncHistory) {
            return null;
        }

        const runId = item.syncHistory.runId;

        return (
            <Link
                onClick={() => void handleDownload(runId)}
                disabled={downloadingRunIds.has(runId)}
                aria-label={format(strings.JobDetails.Panel.downloadAriaLabel, runId)}
            >
                {downloadingRunIds.has(runId)
                    ? strings.JobDetails.Panel.downloadingText
                    : strings.JobDetails.Panel.downloadLinkText}
            </Link>
        );
    };

    const toggleSyncRowExpand = (rowId: string) => {
        setExpandedSyncRowIds((prev) => {
            const next = new Set(prev);
            if (next.has(rowId)) {
                next.delete(rowId);
            } else {
                next.add(rowId);
            }
            return next;
        });
    };

    useEffect(() => {
        if (!isOpen) {
            setExpandedSyncRowIds(new Set());
            setSyncHistoryItems([]);
            setDownloadError(null);
            setDownloadingRunIds(new Set());
            setIsModalOpen(false);
            setModalContent('');
            setModalTitle('');
            setModalViewMode('details');
            setSyncPageNumber(1);
            setTakeActionItem(null);
            setThresholdData(null);
            setIsThresholdDataLoading(false);
            setSelectedUser([]);
            setMatchingRunIds(null);
            setIsUserSearchLoading(false);
            setUserSearchError(null);
            setUserSearchInfo(null);
            setShowProgressUnavailableMessage(false);
            setSearchProgressText(null);
            setUserPickerSuggestions([]);
            setResolvedRunIds(new Set());
            activeSearchRequestIdRef.current = null;
            isSignalRProgressDisabledRef.current = false;
            ignoreNextEmptyUserInputRef.current = false;
            syncHistorySearchSignalRServiceRef.current.stopConnection();
            return;
        }

        setExpandedSyncRowIds(new Set());
        setSyncPageNumber(1);
        dispatch(fetchJobChanges({ syncJobId: jobId }));

        if (!showSyncTab) {
            setSyncHistoryItems([]);
            return;
        }

        dispatch(fetchSyncJobHistory(jobId))
            .unwrap()
            .then((history) => {
                setSyncHistoryItems(history);

                const selectedObjectId = getSelectedUserObjectId();
                if (selectedObjectId) {
                    void searchHistoryForUser(selectedObjectId);
                }
            })
            .catch((error) => {
                console.error(error);
                setSyncHistoryItems([]);
            });
    }, [dispatch, isOpen, jobId, showSyncTab]);

    const handleViewDetails = (details: string) => {
        setModalTitle(strings.JobDetails.Panel.changeDetailsColumnLabel);
        setModalViewMode('details');
        setModalContent(details);
        setIsModalOpen(true);
    };

    const findQueryInChangeDetails = (value: unknown): string | null => {
        if (typeof value === 'string') {
            return value.trim() ? value : null;
        }

        if (!value || typeof value !== 'object') {
            return null;
        }

        const record = value as Record<string, unknown>;
        const directQuery = record.Query ?? record.query;
        if (typeof directQuery === 'string' && directQuery.trim()) {
            return directQuery;
        }

        for (const childValue of Object.values(record)) {
            const query = findQueryInChangeDetails(childValue);
            if (query) {
                return query;
            }
        }

        return null;
    };

    const extractQueryFromChangeDetails = (changeDetails: string): string | null => {
        if (!changeDetails?.trim()) {
            return null;
        }

        try {
            const parsedChangeDetails = JSON.parse(changeDetails);
            return findQueryInChangeDetails(parsedChangeDetails);
        } catch {
            return null;
        }
    };

    const formatQueryForDisplay = (query: string): string => {
        try {
            return JSON.stringify(JSON.parse(query), null, 2);
        } catch {
            return query;
        }
    };

    const handleOpenQuery = (changeDetails: string) => {
        const query = extractQueryFromChangeDetails(changeDetails);
        if (!query) {
            return;
        }

        setModalTitle(strings.JobDetails.Panel.openQuery);
        setModalViewMode('query');
        setModalContent(formatQueryForDisplay(query));
        setIsModalOpen(true);
    };

    const handleCloseModal = () => {
        setIsModalOpen(false);
        setModalContent('');
        setModalTitle('');
        setModalViewMode('details');
    };

    const handleDownload = async (runId: string): Promise<void> => {
        if (downloadingRunIds.has(runId)) {
            return;
        }

        setDownloadError(null);
        setDownloadingRunIds((prev) => new Set(prev).add(runId));

        try {
            await dispatch(downloadMembershipChanges({ syncJobId: jobId, runId, targetGroupId: selectedJob?.targetGroupId ?? '' })).unwrap();
        } catch {
            setDownloadError(strings.JobDetails.Panel.downloadError);
        } finally {
            setDownloadingRunIds((prev) => {
                const next = new Set(prev);
                next.delete(runId);
                return next;
            });
        }
    };

    const parseNestedJson = (obj: unknown): unknown => {
        if (!obj || typeof obj !== 'object') {
            return obj;
        }

        for (const key in obj as Record<string, unknown>) {
            const value = (obj as Record<string, unknown>)[key];

            if (typeof value === 'string') {
                try {
                    (obj as Record<string, unknown>)[key] = JSON.parse(value);
                    parseNestedJson((obj as Record<string, unknown>)[key]);
                } catch {
                    // Leave non-JSON strings unchanged.
                }
            } else if (value && typeof value === 'object') {
                parseNestedJson(value);
            }
        }

        return obj;
    };

    let formattedModalContent;
    if (modalViewMode === 'query') {
        formattedModalContent = modalContent;
    } else {
        try {
            const parsedDetails = JSON.parse(modalContent);
            const cleanedDetails = parseNestedJson(parsedDetails);
            formattedModalContent = JSON.stringify(cleanedDetails, null, 2);
        } catch {
            formattedModalContent = modalContent;
        }
    }

    const configurationColumns: IColumn[] = [
        {
            key: 'changeTime',
            name: strings.JobDetails.Panel.changeTimeColumnLabel,
            fieldName: 'changeTime',
            minWidth: 100,
            maxWidth: 150,
            isResizable: true,
            isMultiline: true,
            onRender: (item: SyncJobChange) => renderTimestamp(item.changeTime),
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
            onRender: (item: SyncJobChange) => <span>{getChangeReasonText(item.changeReason)}</span>,
        },
        {
            key: 'businessJustification',
            name: strings.JobDetails.Panel.businessJustification,
            fieldName: 'businessJustification',
            minWidth: 150,
            maxWidth: 300,
            isResizable: true,
            isMultiline: true,
            onRender: (item: SyncJobChange) => <span>{item.businessJustification}</span>,
        },
        {
            key: 'changeDetails',
            name: strings.JobDetails.Panel.changeDetailsColumnLabel,
            fieldName: 'changeDetails',
            minWidth: 100,
            maxWidth: 150,
            isResizable: true,
            isMultiline: true,
            onRender: (item: SyncJobChange) => (
                <Link onClick={() => handleViewDetails(item.changeDetails)}>
                    {strings.JobDetails.Panel.viewDetails}
                </Link>
            ),
        }
    ];

    const mostRecentThresholdRunId = useMemo<string | null>(() => {
        return syncHistoryItems
            .filter((item) => item.status === RunHistoryStatus.ThresholdExceeded)
            .reduce((latest, item) => {
                const itemTime = getUtcTimestampMillis(item.endTime ?? item.startTime);
                return itemTime > latest.time ? { runId: item.runId, time: itemTime } : latest;
            }, { runId: null as string | null, time: 0 }).runId;
    }, [syncHistoryItems]);

    const isThresholdResolved = useMemo<boolean>(() => {
        if (!mostRecentThresholdRunId) return false;

        const thresholdItem = syncHistoryItems.find((item) => item.runId === mostRecentThresholdRunId);
        if (!thresholdItem) return false;

        const thresholdTime = getUtcTimestampMillis(thresholdItem.endTime ?? thresholdItem.startTime);

        return jobChanges.some(
            (change) =>
                (change.changeReason === SyncJobChangeReason.StatusUpdate ||
                    change.changeReason === SyncJobChangeReason.IgnoreThresholdOnce) &&
                getUtcTimestampMillis(change.changeTime) > thresholdTime
        );
    }, [mostRecentThresholdRunId, syncHistoryItems, jobChanges]);

    const combinedSyncItems = useMemo<CombinedHistoryListItem[]>(() => {
        const configurationItems = jobChanges.map((item, index) => ({
            id: `configuration-${index}-${item.changeTime}`,
            eventType: 'configuration' as const,
            time: item.changeTime,
            statusText: getChangeReasonText(item.changeReason),
            beforeSyncUserCount: null,
            usersAdded: null,
            usersRemoved: null,
            afterSyncUserCount: null,
            jobChange: item,
        }));

        const syncItems = syncHistoryItems.map((item) => ({
            id: `sync-${item.runId}`,
            eventType: 'sync' as const,
            time: item.endTime ?? item.startTime,
            statusText: getStatusDisplayText(item.status),
            beforeSyncUserCount: item.beforeSyncUserCount ?? null,
            usersAdded: item.usersAdded ?? null,
            usersRemoved: item.usersRemoved ?? null,
            afterSyncUserCount: item.afterSyncUserCount ?? null,
            syncHistory: item,
        }));

        return [...configurationItems, ...syncItems].sort(
            (left, right) => getUtcTimestampMillis(right.time) - getUtcTimestampMillis(left.time)
        );
    }, [jobChanges, syncHistoryItems]);

    const sortedSyncItems = useMemo<CombinedHistoryListItem[]>(() => {
        const items = [...combinedSyncItems];

        items.sort((left, right) => {
            let compareValue = 0;

            switch (syncSortKey) {
                case 'time':
                    compareValue = getUtcTimestampMillis(left.time) - getUtcTimestampMillis(right.time);
                    break;
                case 'eventType':
                    compareValue = left.eventType.localeCompare(right.eventType);
                    break;
                case 'status':
                    compareValue = left.statusText.localeCompare(right.statusText);
                    break;
                case 'beforeSyncUserCount':
                    compareValue = (left.beforeSyncUserCount ?? -1) - (right.beforeSyncUserCount ?? -1);
                    break;
                case 'usersAdded':
                    compareValue = (left.usersAdded ?? -1) - (right.usersAdded ?? -1);
                    break;
                case 'usersRemoved':
                    compareValue = (left.usersRemoved ?? -1) - (right.usersRemoved ?? -1);
                    break;
                case 'afterSyncUserCount':
                    compareValue = (left.afterSyncUserCount ?? -1) - (right.afterSyncUserCount ?? -1);
                    break;
            }

            if (compareValue === 0) {
                return getUtcTimestampMillis(right.time) - getUtcTimestampMillis(left.time);
            }

            return isSyncSortDescending ? compareValue * -1 : compareValue;
        });

        return items;
    }, [combinedSyncItems, isSyncSortDescending, syncSortKey]);

    const filteredCombinedSyncItems = useMemo<CombinedHistoryListItem[]>(() => {
        if (selectedUser.length === 0 || matchingRunIds === null) {
            return sortedSyncItems;
        }

        return sortedSyncItems.filter((item) => item.eventType === 'sync'
            && item.syncHistory
            && matchingRunIds.has(item.syncHistory.runId));
    }, [matchingRunIds, selectedUser.length, sortedSyncItems]);

    const totalSyncPages = Math.max(1, Math.ceil(filteredCombinedSyncItems.length / syncPageSize));

    useEffect(() => {
        if (syncPageNumber > totalSyncPages) {
            setSyncPageNumber(totalSyncPages);
        }
    }, [syncPageNumber, totalSyncPages]);

    const pagedSyncItems = useMemo<CombinedHistoryListItem[]>(() => {
        const startIndex = (syncPageNumber - 1) * syncPageSize;
        return filteredCombinedSyncItems.slice(startIndex, startIndex + syncPageSize);
    }, [filteredCombinedSyncItems, syncPageNumber, syncPageSize]);

    const onSyncColumnHeaderClick = (_event?: React.MouseEvent<HTMLElement>, column?: IColumn): void => {
        if (!column || column.key === 'expand') {
            return;
        }

        const nextSortKey = column.key as SyncSortKey;
        const nextIsSortedDescending = syncSortKey === nextSortKey ? !isSyncSortDescending : false;

        setSyncSortKey(nextSortKey);
        setIsSyncSortDescending(nextIsSortedDescending);
        setSyncPageNumber(1);
    };

    const onSyncPageSizeChanged = (_event: React.FormEvent<HTMLDivElement>, option?: IDropdownOption): void => {
        if (!option) {
            return;
        }

        setSyncPageSize(Number(option.key));
        setSyncPageNumber(1);
    };

    const onSyncPageNumberChanged = (_event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string): void => {
        if (!newValue) {
            return;
        }

        const parsedPageNumber = Number(newValue);
        if (Number.isNaN(parsedPageNumber) || parsedPageNumber < 1 || parsedPageNumber > totalSyncPages) {
            return;
        }

        setSyncPageNumber(parsedPageNumber);
    };

    const navigateSyncPage = (direction: number): void => {
        const nextPageNumber = syncPageNumber + direction;
        if (nextPageNumber < 1 || nextPageNumber > totalSyncPages) {
            return;
        }

        setSyncPageNumber(nextPageNumber);
    };

    const renderCombinedStatus = (item: CombinedHistoryListItem): JSX.Element => {
        const isThresholdExceeded = item.syncHistory?.status === RunHistoryStatus.ThresholdExceeded;

        return (
            <div className={classNames.statusCellContainer}>
                <span className={isThresholdExceeded ? classNames.statusCellThresholdExceeded : undefined}>
                    {item.statusText}
                </span>
                
                {isThresholdExceeded && item.syncHistory && item.syncHistory.runId === mostRecentThresholdRunId && !resolvedRunIds.has(item.syncHistory.runId) && !isThresholdResolved && (
                    <Link onClick={() => handleTakeAction(item.syncHistory!)}>{strings.JobDetails.Panel.takeAction}</Link>
                )}
            </div>
        );
    };

    const syncColumns: IColumn[] = [
        {
            key: 'time',
            name: strings.JobDetails.Panel.endTimeColumnLabel,
            fieldName: 'time',
            minWidth: 70,
            isResizable: true,
            isMultiline: true,
            isSorted: syncSortKey === 'time',
            isSortedDescending: isSyncSortDescending,
            onColumnClick: onSyncColumnHeaderClick,
            onRender: (item: CombinedHistoryListItem) => renderTimestamp(item.time),
        },
        {
            key: 'eventType',
            name: strings.JobDetails.Panel.eventTypeColumnLabel,
            fieldName: 'eventType',
            minWidth: 90,
            isResizable: true,
            isMultiline: true,
            isSorted: syncSortKey === 'eventType',
            isSortedDescending: isSyncSortDescending,
            onColumnClick: onSyncColumnHeaderClick,
            onRender: (item: CombinedHistoryListItem) => (
                <span>{item.eventType === 'sync' ? strings.JobDetails.Panel.syncPivotHeader : strings.JobDetails.Panel.configurationPivotHeader}</span>
            ),
        },
        {
            key: 'status',
            name: strings.JobDetails.Panel.statusColumnLabel,
            fieldName: 'statusText',
            minWidth: 70,
            isResizable: true,
            isMultiline: true,
            isSorted: syncSortKey === 'status',
            isSortedDescending: isSyncSortDescending,
            onColumnClick: onSyncColumnHeaderClick,
            onRender: (item: CombinedHistoryListItem) => renderCombinedStatus(item),
        },
        {
            key: 'beforeSyncUserCount',
            name: strings.JobDetails.Panel.beforeSyncUserCountColumnLabel,
            fieldName: 'beforeSyncUserCount',
            minWidth: 60,
            isResizable: true,
            isMultiline: true,
            isSorted: syncSortKey === 'beforeSyncUserCount',
            isSortedDescending: isSyncSortDescending,
            onColumnClick: onSyncColumnHeaderClick,
            onRenderHeader: () => renderMultilineHeader(strings.JobDetails.Panel.beforeSyncUserCountColumnLabel),
            onRender: (item: CombinedHistoryListItem) => renderCount(item.beforeSyncUserCount),
        },
        {
            key: 'usersAdded',
            name: strings.JobDetails.Panel.usersAddedColumnLabel,
            fieldName: 'usersAdded',
            minWidth: 70,
            isResizable: true,
            isSorted: syncSortKey === 'usersAdded',
            isSortedDescending: isSyncSortDescending,
            onColumnClick: onSyncColumnHeaderClick,
            onRender: (item: CombinedHistoryListItem) => renderCount(item.usersAdded),
        },
        {
            key: 'usersRemoved',
            name: strings.JobDetails.Panel.usersRemovedColumnLabel,
            fieldName: 'usersRemoved',
            minWidth: 70,
            isResizable: true,
            isSorted: syncSortKey === 'usersRemoved',
            isSortedDescending: isSyncSortDescending,
            onColumnClick: onSyncColumnHeaderClick,
            onRender: (item: CombinedHistoryListItem) => renderCount(item.usersRemoved),
        },
        {
            key: 'afterSyncUserCount',
            name: strings.JobDetails.Panel.afterSyncUserCountColumnLabel,
            fieldName: 'afterSyncUserCount',
            minWidth: 70,
            isResizable: true,
            isMultiline: true,
            isSorted: syncSortKey === 'afterSyncUserCount',
            isSortedDescending: isSyncSortDescending,
            onColumnClick: onSyncColumnHeaderClick,
            onRender: (item: CombinedHistoryListItem) => renderCount(item.afterSyncUserCount),
        },
        {
            key: 'expand',
            name: '',
            fieldName: '',
            minWidth: 32,
            maxWidth: 32,
            isResizable: false,
            onRender: (item: CombinedHistoryListItem) => {
                const isExpanded = expandedSyncRowIds.has(item.id);
                return (
                    <IconButton
                        iconProps={{ iconName: isExpanded ? 'ChevronUp' : 'ChevronDown' }}
                        ariaLabel={isExpanded
                            ? strings.JobDetails.Panel.collapseRowAriaLabel
                            : strings.JobDetails.Panel.expandRowAriaLabel}
                        onClick={(event) => {
                            event.stopPropagation();
                            toggleSyncRowExpand(item.id);
                        }}
                    />
                );
            },
        },
    ];
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
        setResolveError(null);
    };

    const handleApplyChanges = async (): Promise<void> => {
        if (!thresholdData?.notificationId) {
            return;
        }
        setResolveError(null);
        try {
            await dispatch(resolveNotification({ notificationId: thresholdData.notificationId, resolution: 'IgnoreOnce' })).unwrap();
            setChangesApplied(true);
            const applyRunId = takeActionItem?.runId;
            if (applyRunId) {
                setResolvedRunIds((prev) => new Set(prev).add(applyRunId));
            }
            dispatch(setSelectedJobEnabled(true));
            dispatch(fetchJobChanges({ syncJobId: jobId }));
            handleCloseTakeAction();
        } catch {
            setResolveError(strings.JobDetails.Panel.resolveError);
        }
    };

    const handlePauseSync = async (): Promise<void> => {
        if (!thresholdData?.notificationId) {
            return;
        }
        setResolveError(null);
        try {
            await dispatch(resolveNotification({ notificationId: thresholdData.notificationId, resolution: 'Paused' })).unwrap();
            setSyncPaused(true);
            const pauseRunId = takeActionItem?.runId;
            if (pauseRunId) {
                setResolvedRunIds((prev) => new Set(prev).add(pauseRunId));
            }
            dispatch(fetchJobChanges({ syncJobId: jobId }));
        } catch {
            setResolveError(strings.JobDetails.Panel.resolveError);
        }
    };

    const renderExpandedSyncContent = (item: CombinedHistoryListItem): JSX.Element | null => {
        if (item.eventType === 'sync' && item.syncHistory) {
            const downloadLink = renderDownloadLink(item);

            return (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                        <strong style={{ fontSize: '14px' }}>{strings.JobDetails.Panel.runIdColumnLabel}:</strong>
                        <span style={{ fontSize: '12px' }}>{item.syncHistory.runId}</span>
                    </div>
                    {downloadLink && (
                        <div>
                            {downloadLink}
                        </div>
                    )}
                </div>
            );
        }

        if (item.eventType === 'configuration' && item.jobChange) {
            const query = extractQueryFromChangeDetails(item.jobChange.changeDetails);

            return (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                        <strong style={{ fontSize: '14px' }}>{strings.JobDetails.Panel.changedByColumnLabel}:</strong>
                        <span style={{ fontSize: '12px' }}>{item.jobChange.changedByDisplayName || strings.JobDetails.Panel.emptyValuePlaceholder}</span>
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                        <strong style={{ fontSize: '14px' }}>{strings.JobDetails.Panel.businessJustification}:</strong>
                        <span style={{ fontSize: '12px' }}>{item.jobChange.businessJustification || strings.JobDetails.Panel.emptyValuePlaceholder}</span>
                    </div>
                    {query && (
                        <div>
                            <Link onClick={() => handleOpenQuery(item.jobChange!.changeDetails)}>
                                {strings.JobDetails.Panel.openQuery}
                            </Link>
                        </div>
                    )}
                </div>
            );
        }

        return null;
    };

    const onRenderSyncRow = (rowProps?: IDetailsRowProps): JSX.Element => {
        if (!rowProps) return <></>;

        const item = rowProps.item as CombinedHistoryListItem;
        const isExpanded = expandedSyncRowIds.has(item.id);
        const expandedContent = renderExpandedSyncContent(item);

        return (
            <>
                <DetailsRow
                    {...rowProps}
                    styles={isExpanded && expandedContent ? { root: { borderBottom: 'none' } } : undefined}
                />
                {isExpanded && expandedContent && (
                    <div style={{ padding: '4px 12px 8px 12px', backgroundColor: 'inherit', borderBottom: '1px solid #edebe9' }}>
                        {expandedContent}
                    </div>
                )}
            </>
        );
    };

    return (
        <Panel
            type={PanelType.custom}
            customWidth="760px"
            isLightDismiss
            isOpen={isOpen}
            onDismiss={dismissPanel}
            headerText={strings.JobDetails.Panel.history}
            closeButtonAriaLabel={strings.close}
            layerProps={{ eventBubblingEnabled: true }}
            styles={{
                main: { overflow: 'visible' },
                contentInner: { overflow: 'visible' },
                scrollableContent: { overflow: 'visible' },
            }}
        >
            {changesApplied && (
                <MessageBar
                    messageBarType={MessageBarType.success}
                    onDismiss={() => setChangesApplied(false)}
                >
                    {strings.JobDetails.Panel.changesAppliedSuccess}
                </MessageBar>
            )}
            {syncPaused && (
                <MessageBar
                    messageBarType={MessageBarType.success}
                    onDismiss={() => setSyncPaused(false)}
                >
                    {strings.JobDetails.Panel.syncPausedSuccess}
                </MessageBar>
            )}
            <Pivot>
                <PivotItem
                    headerText={strings.JobDetails.Panel.configurationPivotHeader}
                    headerButtonProps={{
                        'data-order': 1,
                        'data-title': strings.JobDetails.Panel.configurationPivotHeader,
                    }}
                >
                    <DetailsList
                        setKey="configurationSet"
                        columns={configurationColumns}
                        items={jobChanges}
                        selectionMode={0}
                    />
                </PivotItem>
                {showSyncTab && (
                    <PivotItem
                        headerText={strings.JobDetails.Panel.syncPivotHeader}
                        headerButtonProps={{
                            'data-order': 2,
                            'data-title': strings.JobDetails.Panel.syncPivotHeader,
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
                            <div className={classNames.userSearchField} style={{ gridColumn: '1 / -1' }}>
                                <Label className={classNames.userSearchLabel}>{strings.JobDetails.Panel.searchUserLabel}</Label>
                                <NormalPeoplePicker
                                    componentRef={userPickerRef}
                                    key={'normal'}
                                    aria-label={strings.JobDetails.Panel.searchUserLabel}
                                    onRenderSuggestionsItem={renderUserSuggestion}
                                    onResolveSuggestions={getPickerSuggestions}
                                    onInputChange={handleUserSearchInputChange}
                                    onChange={onSelectedUserChanged}
                                    selectedItems={selectedUser}
                                    itemLimit={1}
                                    resolveDelay={600}
                                    inputProps={{ placeholder: strings.JobDetails.Panel.searchUserPlaceholder }}
                                    pickerSuggestionsProps={{
                                        suggestionsClassName: classNames.userSuggestionList,
                                        suggestionsItemClassName: classNames.userSuggestionItem,
                                        resultsMaximumNumber: 5,
                                        noResultsFoundText: strings.JobDetails.Panel.searchUserNoResults,
                                        loadingText: strings.JobDetails.Panel.searchUserLoading,
                                        suggestionsAvailableAlertText: strings.JobDetails.Panel.searchUserLabel,
                                    }}
                                    styles={{
                                        text: classNames.userSearchPicker,
                                    }}
                                    pickerCalloutProps={{
                                        directionalHint: DirectionalHint.bottomLeftEdge,
                                        directionalHintFixed: true,
                                        alignTargetEdge: true,
                                        target: userPickerRef.current?.input?.current?.inputElement ?? undefined,
                                        calloutMinWidth: 220,
                                        calloutMaxWidth: 300,
                                        gapSpace: 4,
                                        coverTarget: false,
                                        doNotLayer: true,
                                        styles: {
                                            root: {
                                                zIndex: 1000,
                                            },
                                            calloutMain: {},
                                        },
                                    }}
                                />
                            </div>
                        </div>
                        {isUserSearchLoading && (
                            <Spinner
                                label={searchProgressText ?? strings.JobDetails.Panel.searchUserLoading}
                                size={SpinnerSize.small}
                            />
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
                            setKey="combinedSyncSet"
                            columns={syncColumns}
                            items={pagedSyncItems}
                            layoutMode={DetailsListLayoutMode.justified}
                            selectionMode={0}
                            onRenderRow={onRenderSyncRow}
                        />
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px', marginTop: '12px', flexWrap: 'wrap' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                <IconButton
                                    iconProps={{ iconName: 'ChevronLeft' }}
                                    title={strings.JobsList.PagingBar.previousPage}
                                    ariaLabel={strings.JobsList.PagingBar.previousPage}
                                    onClick={() => navigateSyncPage(-1)}
                                    disabled={syncPageNumber <= 1}
                                />
                                <span>{strings.JobsList.PagingBar.page}</span>
                                <TextField
                                    ariaLabel={strings.JobsList.PagingBar.pageNumberAriaLabel}
                                    styles={{ root: { width: 56 } }}
                                    value={syncPageNumber.toString()}
                                    onChange={onSyncPageNumberChanged}
                                />
                                <span>{strings.JobsList.PagingBar.of} {totalSyncPages}</span>
                                <IconButton
                                    iconProps={{ iconName: 'ChevronRight' }}
                                    title={strings.JobsList.PagingBar.nextPage}
                                    ariaLabel={strings.JobsList.PagingBar.nextPage}
                                    onClick={() => navigateSyncPage(1)}
                                    disabled={syncPageNumber >= totalSyncPages}
                                />
                            </div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                <span>{strings.JobsList.PagingBar.display}</span>
                                <Dropdown
                                    ariaLabel={strings.JobsList.PagingBar.pageSizeAriaLabel}
                                    selectedKey={syncPageSize}
                                    options={syncPageSizeOptions}
                                    onChange={onSyncPageSizeChanged}
                                    styles={{ dropdown: { width: 90 } }}
                                />
                                <span>{strings.JobsList.PagingBar.items}</span>
                            </div>
                        </div>
                    </PivotItem>
                )}
            </Pivot>
            <Modal
                isOpen={isModalOpen}
                onDismiss={handleCloseModal}
                styles={{
                    main: {
                        width: '85vw',
                        maxWidth: '1100px',
                        height: '80vh',
                    },
                }}
            >
                <div className={classNames.container}>
                    <div className={classNames.header}>
                        <span>{modalTitle || strings.JobDetails.Panel.changeDetailsColumnLabel}</span>
                        <IconButton
                            iconProps={{ iconName: 'Cancel' }}
                            ariaLabel={strings.close}
                            onClick={handleCloseModal}
                        />
                    </div>
                    <TextField
                        value={formattedModalContent}
                        readOnly
                        multiline
                        resizable
                        rows={28}
                        spellCheck={false}
                        styles={{
                            root: { flex: 1, minHeight: 0 },
                            fieldGroup: { minHeight: '65vh' },
                            field: {
                                fontFamily: 'Consolas, "Courier New", monospace',
                                lineHeight: '1.5',
                            },
                        }}
                    />
                </div>
            </Modal>
            <ThresholdExceededActionDialog
                isOpen={takeActionItem !== null}
                isLoading={isThresholdDataLoading}
                onDismiss={handleCloseTakeAction}
                groupName={selectedJob?.targetGroupName ?? ''}
                usersToAdd={thresholdData?.changeQuantityForAdditions ?? 0}
                increasePercentage={thresholdData?.changePercentageForAdditions ?? 0}
                thresholdPercentageForAdditions={thresholdData?.thresholdPercentageForAdditions ?? 0}
                usersToRemove={thresholdData?.changeQuantityForRemovals ?? 0}
                decreasePercentage={thresholdData?.changePercentageForRemovals ?? 0}
                thresholdPercentageForRemovals={thresholdData?.thresholdPercentageForRemovals ?? 0}
                onApplyChanges={handleApplyChanges}
                onEditRules={onEditRules ? () => {
                    handleCloseTakeAction();
                    dismissPanel();
                    onEditRules();
                } : () => {}}
                isEditRulesEnabled={!!onEditRules}
                onEditThreshold={onEditThreshold ? () => {
                    const additionsExceeded = !!thresholdData && thresholdData.changePercentageForAdditions > thresholdData.thresholdPercentageForAdditions;
                    const removalsExceeded = !!thresholdData && thresholdData.changePercentageForRemovals > thresholdData.thresholdPercentageForRemovals;
                    handleCloseTakeAction();
                    dismissPanel();
                    onEditThreshold({ additionsExceeded, removalsExceeded });
                } : () => {}}
                onPauseSync={handlePauseSync}
                isApplyChangesEnabled={!!thresholdData?.notificationId}
                isEditThresholdEnabled={!!onEditThreshold}
                isPauseSyncEnabled={!!thresholdData?.notificationId}
                errorMessage={resolveError ?? undefined}
                purgeDate={thresholdData?.purgeDate}
            />
        </Panel>
    )
};
