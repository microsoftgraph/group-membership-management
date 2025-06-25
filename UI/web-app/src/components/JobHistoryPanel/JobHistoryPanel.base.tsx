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
} from '@fluentui/react';
import {
    IJobHistoryPanelProps, IJobHistoryPanelStyleProps, IJobHistoryPanelStyles,
} from './JobHistoryPanel.types';
import { useStrings } from '../../store/hooks';
import { useDispatch, useSelector } from 'react-redux';
import { AppDispatch } from '../../store';
import { useEffect, useState } from 'react';
import { fetchJobChanges } from '../../store/jobDetails.api';
import { selectSelectedJobChanges } from '../../store/jobs.slice';
import { SyncJobChange } from '../../models/SyncJobChange';
import { SyncJobChangeReason } from '../../models/SyncJobChangeReason';

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
    const columns: IColumn[] = [
        {
            key: 'changeTime',
            name: strings.JobDetails.Panel.changeTimeColumnLabel,
            fieldName: 'changeTime',
            minWidth: 120,
            maxWidth: 200,
            isResizable: true,
            onRender: (item: SyncJobChange) => {
                const utcDate = item.changeTime.endsWith('Z') ? item.changeTime : `${item.changeTime}Z`;
                const utcDateObj = new Date(utcDate);
                const localDate = utcDateObj.toLocaleString();
                return <span>{localDate}</span>;
            }
        },
        {
            key: 'changedByDisplayName',
            name: strings.JobDetails.Panel.changedByColumnLabel,
            fieldName: 'changedByDisplayName',
            minWidth: 120,
            maxWidth: 200,
            isResizable: true,
        },
        {
            key: 'changeReason',
            name: strings.JobDetails.Panel.changeReasonColumnLabel,
            fieldName: 'changeReason',
            minWidth: 120,
            maxWidth: 200,
            isResizable: true,
            onRender: (item: SyncJobChange) => {
                switch (item.changeReason) {
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
                    default:
                        return item.changeReason;
                }
            }
        },
        {
            key: 'businessJustification',
            name: strings.JobDetails.Panel.businessJustification,
            fieldName: 'businessJustification',
            minWidth: 100,
            maxWidth: 200,
            isResizable: true,
            onRender: (item: SyncJobChange) => {
                return <span>{item.businessJustification}</span>;
            }
        },
        {
            key: 'changeDetails',
            name: strings.JobDetails.Panel.changeDetailsColumnLabel,
            fieldName: 'changeDetails',
            minWidth: 100,
            maxWidth: 200,
            isResizable: true,
            onRender: (item: SyncJobChange) => {
                return <Link onClick={() => handleViewDetails(item.changeDetails)}>
                    {strings.JobDetails.Panel.viewDetails}
                </Link>;
            }
        }
    ];

    const classNames: IProcessedStyleSet<IJobHistoryPanelStyles> = getClassNames(styles, { className, theme });

    const dispatch = useDispatch<AppDispatch>();

    const [detailsListItems, setDetailsListItems] = useState<SyncJobChange[]>([]);
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [modalContent, setModalContent] = useState('');

    const jobChanges: SyncJobChange[] | undefined = useSelector(selectSelectedJobChanges);

    useEffect(() => {
        if (isOpen) {
            dispatch(fetchJobChanges({ syncJobId: jobId }));
        }
    }, [isOpen, dispatch, jobId]);

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
}