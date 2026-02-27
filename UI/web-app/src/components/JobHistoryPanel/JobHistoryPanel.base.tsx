// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    classNamesFunction,
    IProcessedStyleSet,
    DetailsList,
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
} from '@fluentui/react';
import {
    IJobHistoryPanelProps, IJobHistoryPanelStyleProps, IJobHistoryPanelStyles,
} from './JobHistoryPanel.types';
import { useStrings } from '../../store/hooks';
import { useDispatch, useSelector } from 'react-redux';
import { AppDispatch } from '../../store';
import { useEffect, useState } from 'react';
import { fetchJobChanges, fetchSyncJobHistory, downloadMembershipChanges } from '../../store/jobDetails.api';
import { selectSelectedJobChanges } from '../../store/jobs.slice';
import { SyncJobChange } from '../../models/SyncJobChange';
import { SyncJobChangeReason } from '../../models/SyncJobChangeReason';
import { SyncJobHistory } from '../../models/SyncJobHistory';
import { selectIsJobTenantReader, selectIsJobTenantWriter, selectIsSubmissionReviewer } from '../../store/roles.slice';
import { renderMultilineHeader } from '../../utils/stringUtils';
import { getStatusDisplayText } from '../../utils/jobUtils';
import { format } from 'react-string-format';

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

    const classNames: IProcessedStyleSet<IJobHistoryPanelStyles> = getClassNames(styles, { className, theme });

    const [downloadError, setDownloadError] = useState<string | null>(null);
    const [downloadingRunIds, setDownloadingRunIds] = useState<Set<string>>(new Set());

    useEffect(() => {
        setDownloadError(null);
    }, [isOpen]);

    const handleDownload = async (runId: string) => {
        if (downloadingRunIds.has(runId)) return;
        setDownloadError(null);
        setDownloadingRunIds(prev => new Set(prev).add(runId));
        try {
            await dispatch(downloadMembershipChanges({ syncJobId: jobId, runId })).unwrap();
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

    const isJobTenantReader = useSelector(selectIsJobTenantReader);
    const isJobTenantWriter = useSelector(selectIsJobTenantWriter);
    const isSubmissionReviewer = useSelector(selectIsSubmissionReviewer);
    const showDownloadColumn = isJobTenantReader || isJobTenantWriter || isSubmissionReviewer;

    const syncHistoryColumns: IColumn[] = [
        {
            key: 'runId',
            name: strings.JobDetails.Panel.runIdColumnLabel,
            fieldName: 'runId',
            minWidth: 100,
            maxWidth: 150,
            isResizable: true,
            isMultiline: true,
        },
        {
            key: 'startTime',
            name: strings.JobDetails.Panel.startTimeColumnLabel,
            fieldName: 'startTime',
            minWidth: 100,
            maxWidth: 150,
            isResizable: true,
            isMultiline: true,
            onRender: (item: SyncJobHistory) => {
                if (!item.startTime) return <span>-</span>;
                const utcDate = item.startTime.endsWith('Z') ? item.startTime : `${item.startTime}Z`;
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
            key: 'endTime',
            name: strings.JobDetails.Panel.endTimeColumnLabel,
            fieldName: 'endTime',
            minWidth: 100,
            maxWidth: 150,
            isResizable: true,
            isMultiline: true,
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
            key: 'duration',
            name: strings.JobDetails.Panel.durationColumnLabel,
            fieldName: 'duration',
            minWidth: 80,
            maxWidth: 120,
            isResizable: true,
            onRender: (item: SyncJobHistory) => {
                return <span>{item.duration ?? '-'}</span>;
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
                return <span>{getStatusDisplayText(item.status)}</span>;
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
            onRenderHeader: () => renderMultilineHeader(strings.JobDetails.Panel.afterSyncUserCountColumnLabel),
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
            onRender: (item: SyncJobHistory) => {
                return <span>{item.thresholdViolations ?? '-'}</span>;
            }
        },
        {
            key: 'updatedByFunction',
            name: strings.JobDetails.Panel.updatedByFunctionColumnLabel,
            fieldName: 'updatedByFunction',
            minWidth: 120,
            maxWidth: 200,
            isResizable: true,
            isMultiline: true,
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

    const jobChanges: SyncJobChange[] | undefined = useSelector(selectSelectedJobChanges);
    const showSyncTab = isJobTenantReader || isJobTenantWriter;

    useEffect(() => {
        if (isOpen) {
            dispatch(fetchJobChanges({ syncJobId: jobId }));
            if (showSyncTab) {
                dispatch(fetchSyncJobHistory(jobId))
                    .unwrap()
                    .then((history) => {
                        setSyncHistoryItems(history);
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
                        <DetailsList
                            setKey="syncHistorySet"
                            columns={syncHistoryColumns}
                            items={syncHistoryItems}
                            selectionMode={0}
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
        </Panel>
    )
};
