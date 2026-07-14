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
    DefaultButton,
    PrimaryButton,
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
    Icon,
    TooltipHost,
} from '@fluentui/react';
import { IPersonaProps } from '@fluentui/react/lib/Persona';
import {
    IJobHistoryPanelProps, IJobHistoryPanelStyleProps, IJobHistoryPanelStyles,
} from './JobHistoryPanel.types';
import { useStrings } from '../../store/hooks';
import { useDispatch, useSelector } from 'react-redux';
import { AppDispatch } from '../../store';
import { useEffect, useMemo, useRef, useState } from 'react';
import { downloadMembershipChanges, fetchJobChanges, fetchSyncJobHistory, fetchThresholdNotification, resolveNotification, searchSyncHistoryByUser, fetchSyncExplanation, fetchRunExplanation } from '../../store/jobDetails.api';
import { selectSelectedJobChanges, selectSelectedJobDetails, setSelectedJobEnabled } from '../../store/jobs.slice';
import { SyncJobChange } from '../../models/SyncJobChange';
import { SyncJobChangeReason } from '../../models/SyncJobChangeReason';
import { SyncJobHistory } from '../../models/SyncJobHistory';
import { SyncHistorySearchProgressUpdate } from '../../models/SyncHistorySearchProgressUpdate';
import { MembershipChangeType, SearchSyncHistoryByUserRunMembershipChange } from '../../models/SearchSyncHistoryByUserResult';
import { ThresholdNotificationData } from '../../models/ThresholdNotificationData';
import { selectIsJobTenantReader, selectIsJobTenantWriter, selectIsGeneralSettingsAdministrator } from '../../store/roles.slice';
import { selectIsAISearchForUserEnabled, selectIsAIRunExplanationEnabled } from '../../store/settings.slice';
import { renderMultilineHeader } from '../../utils/stringUtils';
import { getStatusDisplayText } from '../../utils/jobUtils';
import { RunHistoryStatus } from '../../models/Status';
import { format } from 'react-string-format';
import { ThresholdExceededActionDialog } from '../ThresholdExceededActionDialog';
import { MembershipLookup } from '../MembershipLookup';
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

const RUN_EXPLANATION_FALLBACK = 'The specific reason could not be determined from the available data.';


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
    const isGeneralSettingsAdministrator = useSelector(selectIsGeneralSettingsAdministrator);
    const isAISearchForUserEnabled = useSelector(selectIsAISearchForUserEnabled);
    const isAIRunExplanationEnabled = useSelector(selectIsAIRunExplanationEnabled);
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
    const [eventTypeFilter, setEventTypeFilter] = useState<'all' | 'configuration'>('all');
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
    const [userInGroup, setUserInGroup] = useState<boolean | null>(null);
    const [userManualNote, setUserManualNote] = useState<string | null>(null);
    const [showProgressUnavailableMessage, setShowProgressUnavailableMessage] = useState(false);
    const [searchProgressText, setSearchProgressText] = useState<string | null>(null);
    const [userPickerSuggestions, setUserPickerSuggestions] = useState<IPersonaProps[]>([]);
    const [runMembershipChangeMap, setRunMembershipChangeMap] = useState<Map<string, MembershipChangeType> | null>(null);

    const syncHistorySearchSignalRServiceRef = useRef<SignalRSyncHistorySearchService>(new SignalRSyncHistorySearchService());
    const activeSearchRequestIdRef = useRef<string | null>(null);
    const isSignalRProgressDisabledRef = useRef(false);
    const userPickerRef = useRef<any>(null);
    const ignoreNextEmptyUserInputRef = useRef(false);

    const [aiExplanationCache, setAiExplanationCache] = useState<Map<string, string>>(new Map());
    const [aiExplanationLoading, setAiExplanationLoading] = useState<Set<string>>(new Set());
    const [aiExplanationErrors, setAiExplanationErrors] = useState<Set<string>>(new Set());
    // Per-run AI explanation (keyed by runId, independent of any selected user).
    const [runExplanationCache, setRunExplanationCache] = useState<Map<string, string>>(new Map());
    const [runExplanationLoading, setRunExplanationLoading] = useState<Set<string>>(new Set());
    const [runExplanationErrors, setRunExplanationErrors] = useState<Set<string>>(new Set());
    const previousExpandedSyncRowIdsRef = useRef<Set<string>>(new Set());

    const getChangeReasonText = (changeReason: string | null): string => {
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
                return strings.JobDetails.Panel.thresholdExceededApproved;
            default:
                return changeReason ?? '';
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
        setRunMembershipChangeMap(null);
        setIsUserSearchLoading(false);
        setUserSearchError(null);
        setUserSearchInfo(null);
        setUserInGroup(null);
        setUserManualNote(null);
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

    const getMostRecentMembershipChange = (
        changes: SearchSyncHistoryByUserRunMembershipChange[],
    ): SearchSyncHistoryByUserRunMembershipChange | null => {
        if (changes.length === 0) {
            return null;
        }

        const timestampByRunId = new Map<string, number>();
        for (const item of syncHistoryItems) {
            if (item.runId) {
                timestampByRunId.set(
                    item.runId,
                    getUtcTimestampMillis(item.endTime ?? item.startTime ?? null),
                );
            }
        }

        let bestChange: SearchSyncHistoryByUserRunMembershipChange | null = null;
        let bestTime = -1;

        for (const change of changes) {
            const time = timestampByRunId.get(change.runId);
            if (time === undefined) {
                continue;
            }
            if (time > bestTime) {
                bestTime = time;
                bestChange = change;
            }
        }

        return bestChange;
    };

    const searchHistoryForUser = async (userObjectId: string): Promise<void> => {
        setMatchingRunIds(null);
        setRunMembershipChangeMap(null);
        setSyncPageNumber(1);
        setUserSearchError(null);
        setUserSearchInfo(null);
        setUserInGroup(null);
        setUserManualNote(null);
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

            const changeMap = new Map<string, MembershipChangeType>();
            for (const change of result.runMembershipChanges) {
                changeMap.set(change.runId, change.membershipChangeType);
            }
            setRunMembershipChangeMap(changeMap);

            const actualInGroup = result.checkedCurrentGroupMembership
                ? result.userInCurrentGroup
                : null;

            if (actualInGroup !== null) {
                setUserInGroup(actualInGroup);
                setUserSearchInfo(actualInGroup
                    ? strings.JobDetails.Panel.userCurrentlyInGroupMessage
                    : strings.JobDetails.Panel.userNotInGroupMessage);

                const mostRecentChange = getMostRecentMembershipChange(result.runMembershipChanges);
                if (mostRecentChange) {
                    const lastActionWasAdd = mostRecentChange.membershipChangeType === MembershipChangeType.Added;
                    if (actualInGroup && !lastActionWasAdd) {
                        setUserManualNote(strings.JobDetails.Panel.userManuallyAddedNote);
                    } else if (!actualInGroup && lastActionWasAdd) {
                        setUserManualNote(strings.JobDetails.Panel.userManuallyRemovedNote);
                    }
                }
            } else if (result.matchingRunIds.length > 0) {
                const mostRecentChange = getMostRecentMembershipChange(result.runMembershipChanges);
                if (mostRecentChange) {
                    const inferredInGroup = mostRecentChange.membershipChangeType === MembershipChangeType.Added;
                    setUserInGroup(inferredInGroup);
                    setUserSearchInfo(inferredInGroup
                        ? strings.JobDetails.Panel.userCurrentlyInGroupMessage
                        : strings.JobDetails.Panel.userNotInGroupMessage);
                }
            }
        } catch {
            if (activeSearchRequestIdRef.current !== requestId) {
                return;
            }

            setMatchingRunIds(new Set());
            setRunMembershipChangeMap(null);
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
        setUserInGroup(null);
        setUserManualNote(null);
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
            setRunMembershipChangeMap(null);
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

    const renderCount = (item: CombinedHistoryListItem, value: number | null): JSX.Element => {
        if (item.eventType === 'configuration') {
            return <></>;
        }

        return <span>{value ?? strings.JobDetails.Panel.emptyValuePlaceholder}</span>;
    };

    const renderHighlightedCount = (
        item: CombinedHistoryListItem,
        value: number | null,
        highlightChangeType: MembershipChangeType,
    ): JSX.Element => {
        if (item.eventType === 'configuration') {
            return <></>;
        }

        const runId = item.syncHistory?.runId;
        const changeType = runId && runMembershipChangeMap ? runMembershipChangeMap.get(runId) : undefined;
        const isHighlighted = changeType === highlightChangeType;
        const shouldShowPendingMarker = value !== null && value > 0 && showPendingMarker(item);
        let countElement: JSX.Element;

        if (isHighlighted && value !== null) {
            const highlightClass = highlightChangeType === MembershipChangeType.Added
                ? classNames.highlightedAddedCell
                : classNames.highlightedRemovedCell;
            countElement = (
                <span
                    className={highlightClass}
                    aria-label={highlightChangeType === MembershipChangeType.Added
                        ? strings.JobDetails.Panel.userAddedInSyncAriaLabel.replace('{0}', String(value))
                        : strings.JobDetails.Panel.userRemovedInSyncAriaLabel.replace('{0}', String(value))}
                >
                    {value}
                </span>
            );
        } else {
            countElement = <span>{value ?? strings.JobDetails.Panel.emptyValuePlaceholder}</span>;
        }

        if (!shouldShowPendingMarker) {
            return countElement;
        }

        return (
            <span className={classNames.pendingCell}>
                {countElement}
                <span className={classNames.pendingMarker} aria-label={strings.JobDetails.Panel.pendingMarkerAriaLabel}>
                    {strings.JobDetails.Panel.pendingMarkerLabel}
                </span>
            </span>
        );
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
        const hasPendingThresholdChanges = canTakeActionOnRow(item);
        const isDownloading = downloadingRunIds.has(runId);

        if (hasPendingThresholdChanges) {
            return (
                <DefaultButton
                    iconProps={{ iconName: 'Download' }}
                    onClick={() => void handleDownload(runId)}
                    disabled={isDownloading}
                    aria-label={format(strings.JobDetails.Panel.downloadAriaLabel, runId)}
                    styles={{ root: { borderRadius: 4 } }}
                    text={isDownloading
                        ? strings.JobDetails.Panel.downloadingText
                        : strings.JobDetails.Panel.downloadPendingUsersLinkText}
                />
            );
        }

        return (
            <Link
                onClick={() => void handleDownload(runId)}
                disabled={isDownloading}
                aria-label={format(strings.JobDetails.Panel.downloadAriaLabel, runId)}
            >
                {isDownloading
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
            setEventTypeFilter('all');
            setTakeActionItem(null);
            setThresholdData(null);
            setIsThresholdDataLoading(false);
            setChangesApplied(false);
            setSelectedUser([]);
            setMatchingRunIds(null);
            setRunMembershipChangeMap(null);
            setIsUserSearchLoading(false);
            setUserSearchError(null);
            setUserSearchInfo(null);
            setUserInGroup(null);
            setUserManualNote(null);
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

    const handleViewDetails = (details: string | null) => {
        setModalTitle(strings.JobDetails.Panel.changeDetailsColumnLabel);
        setModalViewMode('details');
        setModalContent(details ?? '');
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

    const extractQueryFromChangeDetails = (changeDetails: string | null): string | null => {
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

    const handleOpenQuery = (changeDetails: string | null) => {
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
            onRender: (item: SyncJobChange) => <span>{item.changedByDisplayName ?? ''}</span>,
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

    const isThresholdAddressed = useMemo<boolean>(() => {
        if (!mostRecentThresholdRunId) return false;

        const thresholdItem = syncHistoryItems.find((item) => item.runId === mostRecentThresholdRunId);
        if (!thresholdItem) return false;

        const thresholdTime = getUtcTimestampMillis(thresholdItem.endTime ?? thresholdItem.startTime);
        return jobChanges.some(
            (change) =>
                (
                    change.changeReason === SyncJobChangeReason.StatusUpdate ||
                    change.changeReason === SyncJobChangeReason.Update ||
                    change.changeReason === SyncJobChangeReason.SubmissionApproved ||
                    change.changeReason === SyncJobChangeReason.SubmissionRejected ||
                    change.changeReason === SyncJobChangeReason.IgnoreThresholdOnce
                ) &&
                getUtcTimestampMillis(change.changeTime) > thresholdTime
        );
    }, [mostRecentThresholdRunId, syncHistoryItems, jobChanges]);

    const showPendingMarker = (item: CombinedHistoryListItem): boolean =>
        item.eventType === 'sync' &&
        item.syncHistory?.status === RunHistoryStatus.ThresholdExceeded;

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
        let items = sortedSyncItems;

        if (eventTypeFilter !== 'all') {
            items = items.filter((item) => item.eventType === eventTypeFilter);
        }

        if (selectedUser.length === 0 || matchingRunIds === null) {
            return items;
        }

        return items.filter((item) => item.eventType === 'sync'
            && item.syncHistory
            && matchingRunIds.has(item.syncHistory.runId));
    }, [eventTypeFilter, matchingRunIds, selectedUser.length, sortedSyncItems]);

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

    useEffect(() => {
        if (!isAIRunExplanationEnabled) {
            previousExpandedSyncRowIdsRef.current = new Set(expandedSyncRowIds);
            return;
        }
        const previous = previousExpandedSyncRowIdsRef.current;
        const newlyExpanded: string[] = [];
        expandedSyncRowIds.forEach((rowId) => {
            if (!previous.has(rowId)) {
                newlyExpanded.push(rowId);
            }
        });
        previousExpandedSyncRowIdsRef.current = new Set(expandedSyncRowIds);
        if (newlyExpanded.length === 0) return;

        const runIdsToInvalidate: string[] = [];
        newlyExpanded.forEach((rowId) => {
            const item = pagedSyncItems.find((i) => i.id === rowId);
            if (!item || item.eventType !== 'sync' || !item.syncHistory) return;
            const runId = item.syncHistory.runId;
            if (runExplanationCache.get(runId) === RUN_EXPLANATION_FALLBACK) {
                runIdsToInvalidate.push(runId);
            }
        });
        if (runIdsToInvalidate.length === 0) return;

        setRunExplanationCache((prev) => {
            const next = new Map(prev);
            runIdsToInvalidate.forEach((runId) => next.delete(runId));
            return next;
        });
        setRunExplanationErrors((prev) => {
            const next = new Set(prev);
            runIdsToInvalidate.forEach((runId) => next.delete(runId));
            return next;
        });
    }, [expandedSyncRowIds, pagedSyncItems, isAIRunExplanationEnabled, runExplanationCache]);

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

    const canTakeActionOnRow = (item: CombinedHistoryListItem): boolean =>
        item.syncHistory?.status === RunHistoryStatus.ThresholdExceeded &&
        !!item.syncHistory &&
        item.syncHistory.runId === mostRecentThresholdRunId &&
        !resolvedRunIds.has(item.syncHistory.runId) &&
        !isThresholdAddressed;

    const renderCombinedStatus = (item: CombinedHistoryListItem): JSX.Element => {
        const requiresThresholdAction = canTakeActionOnRow(item);
        const isThresholdApproved =
            item.jobChange?.changeReason === SyncJobChangeReason.IgnoreThresholdOnce;
        const statusClassName = requiresThresholdAction
            ? classNames.statusCellThresholdExceeded
            : isThresholdApproved
                ? classNames.statusCellThresholdApproved
                : undefined;

        return (
            <div className={classNames.statusCellContainer}>
                <span className={statusClassName}>
                    {item.statusText}
                </span>
                {requiresThresholdAction && (
                    <span style={{ fontSize: '11px', lineHeight: '14px', color: theme.palette.neutralTertiary }}>
                        {strings.JobDetails.Panel.syncPausedUntilReviewed}
                    </span>
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
            minWidth: 140,
            maxWidth: 170,
            isResizable: true,
            isMultiline: true,
            isSorted: syncSortKey === 'eventType',
            isSortedDescending: isSyncSortDescending,
            onColumnClick: onSyncColumnHeaderClick,
            onRender: (item: CombinedHistoryListItem) => {
                const isSync = item.eventType === 'sync';
                return (
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px' }}>
                        {isSync ? (
                            <svg width="14" height="14" viewBox="0 0 20 20" fill={theme.palette.neutralPrimary} aria-hidden="true">
                                <path d="M10 3a7 7 0 0 1 6.32 4H14a.5.5 0 0 0 0 1h3.5a.5.5 0 0 0 .5-.5V4a.5.5 0 0 0-1 0v1.68A8 8 0 0 0 2.06 9.3a.5.5 0 1 0 .99.13A7 7 0 0 1 10 3zm7.94 7.57a.5.5 0 0 0-.99-.13A7 7 0 0 1 3.68 13H6a.5.5 0 0 0 0-1H2.5a.5.5 0 0 0-.5.5V16a.5.5 0 0 0 1 0v-1.68a8 8 0 0 0 14.94-3.75z" />
                            </svg>
                        ) : (
                            <Icon iconName="Settings" style={{ fontSize: '14px', color: theme.palette.neutralPrimary }} />
                        )}
                        <span style={{ fontWeight: 600, whiteSpace: 'nowrap' }}>{isSync ? strings.JobDetails.Panel.syncPivotHeader : strings.JobDetails.Panel.configurationPivotHeader}</span>
                    </span>
                );
            },
        },
        {
            key: 'status',
            name: strings.JobDetails.Panel.statusColumnLabel,
            fieldName: 'statusText',
            minWidth: 130,
            maxWidth: 170,
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
            onRender: (item: CombinedHistoryListItem) => renderCount(item, item.beforeSyncUserCount),
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
            onRender: (item: CombinedHistoryListItem) => renderHighlightedCount(item, item.usersAdded, MembershipChangeType.Added),
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
            onRender: (item: CombinedHistoryListItem) => renderHighlightedCount(item, item.usersRemoved, MembershipChangeType.Removed),
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
            onRender: (item: CombinedHistoryListItem) => renderCount(item, item.afterSyncUserCount),
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
                        styles={{
                            root: { color: theme.palette.neutralSecondary, backgroundColor: 'transparent' },
                            rootHovered: { color: theme.palette.neutralPrimary, backgroundColor: 'transparent' },
                            rootPressed: { color: theme.palette.neutralPrimary, backgroundColor: 'transparent' },
                            rootFocused: { backgroundColor: 'transparent' },
                            icon: { color: theme.palette.neutralSecondary },
                            iconHovered: { color: theme.palette.neutralPrimary },
                            iconPressed: { color: theme.palette.neutralPrimary },
                        }}
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

    const eventTypeFilterOptions: IDropdownOption[] = [
        { key: 'all', text: strings.JobDetails.Panel.eventTypeAllOption },
        { key: 'configuration', text: strings.JobDetails.Panel.eventTypeConfigurationOption },
    ];

    const hasActiveFilters = eventTypeFilter !== 'all' || selectedUser.length > 0;

    const handleClearFilters = (): void => {
        setEventTypeFilter('all');
        if (selectedUser.length > 0) {
            onSelectedUserChanged([]);
        }
        setSyncPageNumber(1);
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

    const fetchExplanationForRun = (runId: string, userObjectId: string): void => {
        const cacheKey = `${runId}-${userObjectId}`;
        if (aiExplanationCache.has(cacheKey) || aiExplanationLoading.has(cacheKey)) {
            return;
        }

        setAiExplanationLoading((prev) => new Set(prev).add(cacheKey));
        setAiExplanationErrors((prev) => {
            const next = new Set(prev);
            next.delete(cacheKey);
            return next;
        });

        dispatch(fetchSyncExplanation({ syncJobId: jobId, runId, userObjectId }))
            .unwrap()
            .then((result) => {
                setAiExplanationCache((prev) => new Map(prev).set(cacheKey, result.explanation));
            })
            .catch(() => {
                setAiExplanationErrors((prev) => new Set(prev).add(cacheKey));
            })
            .finally(() => {
                setAiExplanationLoading((prev) => {
                    const next = new Set(prev);
                    next.delete(cacheKey);
                    return next;
                });
            });
    };

    const renderAiExplanation = (runId: string): JSX.Element | null => {
        const userObjectId = getSelectedUserObjectId();
        if (!userObjectId || !matchingRunIds || !matchingRunIds.has(runId)) {
            return null;
        }

        const cacheKey = `${runId}-${userObjectId}`;
        const isLoading = aiExplanationLoading.has(cacheKey);
        const hasError = aiExplanationErrors.has(cacheKey);
        const explanation = aiExplanationCache.get(cacheKey);

        if (!isLoading && !hasError && !explanation) {
            queueMicrotask(() => fetchExplanationForRun(runId, userObjectId));
            return (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                    <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.aiDescriptionLabel}</strong>
                    <Spinner size={SpinnerSize.small} label={strings.JobDetails.Panel.aiDescriptionLoading} labelPosition="right" styles={{ root: { justifyContent: 'flex-start' } }} />
                </div>
            );
        }

        if (isLoading) {
            return (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                    <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.aiDescriptionLabel}</strong>
                    <Spinner size={SpinnerSize.small} label={strings.JobDetails.Panel.aiDescriptionLoading} labelPosition="right" styles={{ root: { justifyContent: 'flex-start' } }} />
                </div>
            );
        }

        if (hasError) {
            return (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                    <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.aiDescriptionLabel}</strong>
                    <span style={{ fontSize: '12px', color: theme.palette.redDark }}>{strings.JobDetails.Panel.aiDescriptionError}</span>
                </div>
            );
        }

        if (explanation) {
            return (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                    <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.aiDescriptionLabel}</strong>
                    <span style={{ fontSize: '12px' }}>{explanation}</span>
                </div>
            );
        }

        return null;
    };

    const fetchExplanationForRunOnly = (runId: string): void => {
        if (runExplanationCache.has(runId) || runExplanationLoading.has(runId)) {
            return;
        }

        setRunExplanationLoading((prev) => new Set(prev).add(runId));
        setRunExplanationErrors((prev) => {
            const next = new Set(prev);
            next.delete(runId);
            return next;
        });

        dispatch(fetchRunExplanation({ syncJobId: jobId, runId }))
            .unwrap()
            .then((result) => {
                setRunExplanationCache((prev) => new Map(prev).set(runId, result.explanation));
            })
            .catch(() => {
                setRunExplanationErrors((prev) => new Set(prev).add(runId));
            })
            .finally(() => {
                setRunExplanationLoading((prev) => {
                    const next = new Set(prev);
                    next.delete(runId);
                    return next;
                });
            });
    };

    const renderRunAiExplanation = (runId: string): JSX.Element | null => {
        if (!isAIRunExplanationEnabled) {
            return null;
        }

        const isLoading = runExplanationLoading.has(runId);
        const hasError = runExplanationErrors.has(runId);
        const hasCachedEntry = runExplanationCache.has(runId);
        const explanation = runExplanationCache.get(runId);

        if (!isLoading && !hasError && !hasCachedEntry) {
            queueMicrotask(() => fetchExplanationForRunOnly(runId));
            return (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                    <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.aiDescriptionLabel}</strong>
                    <Spinner size={SpinnerSize.small} label={strings.JobDetails.Panel.aiDescriptionLoading} labelPosition="right" styles={{ root: { justifyContent: 'flex-start' } }} />
                </div>
            );
        }

        if (isLoading) {
            return (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                    <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.aiDescriptionLabel}</strong>
                    <Spinner size={SpinnerSize.small} label={strings.JobDetails.Panel.aiDescriptionLoading} labelPosition="right" styles={{ root: { justifyContent: 'flex-start' } }} />
                </div>
            );
        }

        if (hasError) {
            return (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                    <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.aiDescriptionLabel}</strong>
                    <span style={{ fontSize: '12px', color: theme.palette.redDark }}>{strings.JobDetails.Panel.aiDescriptionError}</span>
                </div>
            );
        }

        // Empty explanation -> Skip Path A on the backend (nothing to explain). Render nothing.
        if (!explanation) {
            return null;
        }

        return (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.aiDescriptionLabel}</strong>
                <span style={{ fontSize: '12px' }}>{explanation}</span>
            </div>
        );
    };

    const renderExpandedSyncContent = (item: CombinedHistoryListItem): JSX.Element | null => {
        if (item.eventType === 'sync' && item.syncHistory) {
            const downloadLink = renderDownloadLink(item);
            // Per-user description wins when a user search is active and this run matches; per-run fills in elsewhere.
            const aiExplanation = renderAiExplanation(item.syncHistory.runId) ?? renderRunAiExplanation(item.syncHistory.runId);
            const customMessage = item.syncHistory.customMessage;
            const showAdfRunId = isGeneralSettingsAdministrator && !!item.syncHistory.adfRunId;
            const showTakeAction = canTakeActionOnRow(item);

            return (
                <div style={{ display: 'flex' }}>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', flex: '0 0 52%' }}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                            <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.runIdColumnLabel}:</strong>
                            <span style={{ fontSize: '12px' }}>{item.syncHistory.runId}</span>
                        </div>
                        {showAdfRunId && (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.adfRunIdColumnLabel}:</strong>
                                <span style={{ fontSize: '12px' }}>{item.syncHistory.adfRunId}</span>
                            </div>
                        )}
                        {downloadLink && (
                            <div style={{ marginTop: 'auto' }}>
                                {downloadLink}
                            </div>
                        )}
                    </div>
                    {(aiExplanation || customMessage || showTakeAction) && (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', flex: '1 1 0' }}>
                            {aiExplanation}
                            {customMessage && (
                                <span style={{ fontSize: '12px', color: theme.palette.redDark }}>{customMessage}</span>
                            )}
                            {showTakeAction && (
                                <div style={{ marginTop: 'auto' }}>
                                    {customMessage ? (
                                        <TooltipHost content={strings.JobDetails.Panel.takeActionDisabledTooltip}>
                                            <PrimaryButton
                                                iconProps={{ iconName: 'Search' }}
                                                text={strings.JobDetails.Panel.reviewAndTakeAction}
                                                disabled
                                                styles={{ root: { borderRadius: 4, alignSelf: 'flex-start' } }}
                                            />
                                        </TooltipHost>
                                    ) : (
                                        <PrimaryButton
                                            iconProps={{ iconName: 'Search' }}
                                            text={strings.JobDetails.Panel.reviewAndTakeAction}
                                            onClick={() => handleTakeAction(item.syncHistory!)}
                                            styles={{ root: { borderRadius: 4, alignSelf: 'flex-start' } }}
                                        />
                                    )}
                                </div>
                            )}
                        </div>
                    )}
                </div>
            );
        }

        if (item.eventType === 'configuration' && item.jobChange) {
            const query = extractQueryFromChangeDetails(item.jobChange.changeDetails);
            const showReviewThresholdCallout = !!onEditThreshold;

            return (
                <div style={{ display: 'flex', flexDirection: 'column' }}>
                    <div style={{ display: 'flex' }}>
                        <div style={{ flex: '0 0 52%' }} />
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', flex: '1 1 0' }}>
                            <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.businessJustification}:</strong>
                            <span style={{ fontSize: '12px' }}>{item.jobChange.businessJustification || strings.JobDetails.Panel.emptyValuePlaceholder}</span>
                        </div>
                    </div>
                    {(query || showReviewThresholdCallout) && (
                        <div className={classNames.configurationActionsRow}>
                            <div className={classNames.configurationQueryAction}>
                                {query && (
                                    <Link onClick={() => handleOpenQuery(item.jobChange!.changeDetails)}>
                                        {strings.JobDetails.Panel.openQuery}
                                    </Link>
                                )}
                            </div>
                            {showReviewThresholdCallout && (
                                <div className={classNames.reviewThresholdCallout}>
                                    <span className={classNames.reviewThresholdCalloutText}>
                                        {strings.JobDetails.Panel.reviewAlertThresholdsCallout}
                                    </span>
                                    <DefaultButton
                                        text={strings.JobDetails.Panel.reviewAlertThresholdsButton}
                                        onClick={() => onEditThreshold?.({ additionsExceeded: false, removalsExceeded: false })}
                                        styles={{
                                            root: {
                                                borderRadius: 2,
                                                flexShrink: 0,
                                                height: 24,
                                                minWidth: 0,
                                                padding: '0 8px',
                                                whiteSpace: 'nowrap',
                                            },
                                            label: {
                                                fontSize: '12px',
                                                fontWeight: 400,
                                                lineHeight: '22px',
                                                margin: 0,
                                                whiteSpace: 'nowrap',
                                            },
                                        }}
                                    />
                                </div>
                            )}
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
        const expandedContent = isExpanded ? renderExpandedSyncContent(item) : null;
        const modifiedBy = isExpanded && item.eventType === 'configuration' ? item.jobChange?.changedByDisplayName : undefined;

        return (
            <>
                <div style={{ position: 'relative' }}>
                    <DetailsRow
                        {...rowProps}
                        styles={{
                            root: {
                                ...(isExpanded && expandedContent ? { borderBottom: 'none' } : {}),
                                backgroundColor: theme.palette.white,
                                selectors: { ':hover': { backgroundColor: theme.palette.white } },
                            },
                        }}
                    />
                    {modifiedBy && (
                        <div style={{ position: 'absolute', top: 0, bottom: 0, left: '52%', right: 44, display: 'flex', alignItems: 'center', pointerEvents: 'none' }}>
                            <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                <strong style={{ fontSize: '14px', fontWeight: 600 }}>{strings.JobDetails.Panel.changedByColumnLabel}:</strong>
                                <span style={{ fontSize: '12px' }}>{modifiedBy}</span>
                            </div>
                        </div>
                    )}
                </div>
                {isExpanded && expandedContent && (
                    <div style={{ padding: '4px 12px 8px 12px', backgroundColor: theme.palette.neutralLighter, borderBottom: `1px solid ${theme.palette.neutralLight}` }}>
                        {expandedContent}
                    </div>
                )}
            </>
        );
    };

    return (
        <Panel
            type={PanelType.custom}
            customWidth="900px"
            isLightDismiss
            isOpen={isOpen}
            onDismiss={() => {
                if (takeActionItem !== null) {
                    handleCloseTakeAction();
                } else {
                    dismissPanel();
                }
            }}
            headerText={takeActionItem !== null
                ? strings.JobDetails.Panel.ThresholdExceededActionDialog.title
                : strings.JobDetails.Panel.history}
            closeButtonAriaLabel={strings.close}
            layerProps={{ eventBubblingEnabled: true }}
            styles={{
                main: { overflow: 'visible' },
                contentInner: { overflow: 'visible' },
                scrollableContent: {
                    overflowY: 'auto',
                    overflowX: 'visible',
                    maxHeight: '100vh',
                    scrollbarGutter: 'stable',
                },
            }}
        >
            {takeActionItem === null && <div className={classNames.headerDivider} />}
            {changesApplied && (
                <MessageBar
                    messageBarType={MessageBarType.success}
                    styles={{
                        root: {
                            borderRadius: 10,
                            marginBottom: 8,
                            overflow: 'hidden',
                        },
                    }}
                >
                    {strings.JobDetails.Panel.notificationResolved}
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
            {takeActionItem !== null ? (
                <ThresholdExceededActionDialog
                    isOpen={takeActionItem !== null}
                    isLoading={isThresholdDataLoading}
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
                    onEditAlertThresholds={onEditThreshold ? () => {
                        const additionsExceeded = !!thresholdData && thresholdData.changePercentageForAdditions > thresholdData.thresholdPercentageForAdditions;
                        const removalsExceeded = !!thresholdData && thresholdData.changePercentageForRemovals > thresholdData.thresholdPercentageForRemovals;
                        handleCloseTakeAction();
                        dismissPanel();
                        onEditThreshold({ additionsExceeded, removalsExceeded });
                    } : () => {}}
                    isApplyChangesEnabled={!!thresholdData?.notificationId}
                    isEditAlertThresholdsEnabled={!!onEditThreshold}
                    errorMessage={resolveError ?? undefined}
                    purgeDate={thresholdData?.purgeDate}
                    aiDescription={isAIRunExplanationEnabled && takeActionItem?.runId && runExplanationCache.get(takeActionItem.runId) !== RUN_EXPLANATION_FALLBACK ? runExplanationCache.get(takeActionItem.runId) : undefined}
                    isAiDescriptionLoading={isAIRunExplanationEnabled && !!takeActionItem?.runId && runExplanationLoading.has(takeActionItem.runId)}
                    aiDescriptionError={isAIRunExplanationEnabled && !!takeActionItem?.runId && runExplanationErrors.has(takeActionItem.runId)}
                    membershipLookup={takeActionItem?.runId ? (
                        <MembershipLookup syncJobId={jobId} runId={takeActionItem.runId} />
                    ) : undefined}
                />
            ) : (
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
                            <div className={classNames.eventTypeFilterField}>
                                <Label>{strings.JobDetails.Panel.eventTypeFilterLabel}</Label>
                                <Dropdown
                                    ariaLabel={strings.JobDetails.Panel.eventTypeFilterLabel}
                                    selectedKey={eventTypeFilter}
                                    options={eventTypeFilterOptions}
                                    onRenderTitle={(options) => {
                                        const selectedOption = options?.[0];
                                        return (
                                            <span>
                                                {selectedOption?.key === 'all'
                                                    ? strings.JobDetails.Panel.eventTypeAllSelectedOption
                                                    : selectedOption?.text}
                                            </span>
                                        );
                                    }}
                                    onChange={(_event, option) => {
                                        if (option) {
                                            setEventTypeFilter(option.key as 'all' | 'configuration');
                                            setSyncPageNumber(1);
                                        }
                                    }}
                                />
                            </div>
                            {isAISearchForUserEnabled ? (
                                <div className={classNames.userSearchField}>
                                    <Label className={classNames.userSearchLabel}>{strings.JobDetails.Panel.searchUserLabel}</Label>
                                    <div className={classNames.userSearchInputWrapper}>
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
                                        <Icon
                                            iconName="Contact"
                                            className={classNames.userSearchTrailingIcon}
                                            aria-hidden={true}
                                        />
                                    </div>
                                </div>
                            ) : <div />}
                            <div className={classNames.filterActionsBar}>
                                <button
                                    type="button"
                                    className={hasActiveFilters
                                        ? classNames.clearFiltersLink
                                        : `${classNames.clearFiltersLink} ${classNames.clearFiltersLinkDisabled}`}
                                    onClick={handleClearFilters}
                                    disabled={!hasActiveFilters}
                                    aria-label={strings.JobDetails.Panel.clearFilters}
                                >
                                    <span className={classNames.clearFiltersIconWrapper} aria-hidden={true}>
                                        <svg
                                            width="24"
                                            height="20"
                                            viewBox="0 0 24 20"
                                            fill="none"
                                            xmlns="http://www.w3.org/2000/svg"
                                        >
                                            <line x1="1" y1="8" x2="14" y2="8" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
                                            <line x1="2.5" y1="13" x2="12.5" y2="13" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
                                            <line x1="4" y1="18" x2="11" y2="18" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
                                            <circle cx="17" cy="6" r="5.5" fill="currentColor" />
                                            <path
                                                d="M14.7 5.15a2.55 2.55 0 0 1 4.7-.2"
                                                stroke="#ffffff"
                                                strokeWidth="0.9"
                                                strokeLinecap="round"
                                                fill="none"
                                            />
                                            <path
                                                d="M19.4 4.95l0.25-1.3-1.3 0.25"
                                                stroke="#ffffff"
                                                strokeWidth="0.9"
                                                strokeLinecap="round"
                                                strokeLinejoin="round"
                                                fill="none"
                                            />
                                            <path
                                                d="M19.3 6.85a2.55 2.55 0 0 1-4.7 0.2"
                                                stroke="#ffffff"
                                                strokeWidth="0.9"
                                                strokeLinecap="round"
                                                fill="none"
                                            />
                                            <path
                                                d="M14.6 7.05l-0.25 1.3 1.3-0.25"
                                                stroke="#ffffff"
                                                strokeWidth="0.9"
                                                strokeLinecap="round"
                                                strokeLinejoin="round"
                                                fill="none"
                                            />
                                        </svg>
                                    </span>
                                    <span>{strings.JobDetails.Panel.clearFilters}</span>
                                </button>
                            </div>
                        </div>
                        {isAISearchForUserEnabled && (<>
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
                            <div
                                className={classNames.userSearchBanner}
                                style={userInGroup === false ? { backgroundColor: theme.semanticColors.warningBackground } : undefined}
                            >
                                <Icon
                                    iconName={userInGroup ? 'CompletedSolid' : 'Warning'}
                                    className={classNames.userSearchBannerIcon}
                                    style={{ color: userInGroup ? theme.palette.themePrimary : theme.palette.neutralPrimary }}
                                />
                                <span className={classNames.userSearchBannerText}>
                                    {userSearchInfo}
                                </span>
                                <span className={classNames.userSearchBannerNote}>
                                    <strong style={{ fontWeight: 600 }}>{strings.JobDetails.Panel.syncHistoryRetentionNoteLabel}</strong> {strings.JobDetails.Panel.syncHistoryRetentionNote}
                                </span>
                                {userManualNote && (
                                    <span className={classNames.userSearchBannerNote} style={{ fontStyle: 'italic' }}>
                                        {userManualNote}
                                    </span>
                                )}
                            </div>
                        )}
                        </>)}
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
            )}
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
        </Panel>
    )
};
