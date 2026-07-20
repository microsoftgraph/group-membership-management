import React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { JobHistoryPanelBase } from './JobHistoryPanel.base';
import { MembershipChangeType } from '../../models/SearchSyncHistoryByUserResult';
import { RunHistoryStatus } from '../../models/Status';
import { SyncJobChangeReason } from '../../models/SyncJobChangeReason';
import type { SyncJobHistory } from '../../models/SyncJobHistory';
import { SettingKey } from '../../models/SettingKey';

const mockTheme = {
  palette: {
    themePrimary: '#0078d4',
    neutralPrimary: '#323130',
    neutralLight: '#edebe9',
  },
  semanticColors: {
    warningBackground: '#fff4ce',
  },
};

const strings = {
  close: 'Close',
  JobsList: {
    PagingBar: {
      previousPage: 'Previous page',
      nextPage: 'Next page',
      page: 'Page',
      of: 'of',
      display: 'Display',
      items: 'items',
      pageNumberAriaLabel: 'Page number',
      pageSizeAriaLabel: 'Page size',
    },
  },
  JobDetails: {
    Panel: {
      history: 'History',
      configurationPivotHeader: 'Configuration',
      syncPivotHeader: 'Sync',
      eventTypeFilterLabel: 'Event type',
      eventTypeAllOption: 'Sync and Configuration (Last 30 days)',
      eventTypeAllSelectedOption: 'Sync and Configuration',
      eventTypeConfigurationOption: 'Configuration (All time)',
      searchUserLabel: 'Search for a user',
      searchUserPlaceholder: 'Search',
      searchUserNoResults: 'No results',
      searchUserLoading: 'Searching user history',
      searchUserError: 'Unable to search user history',
      searchUserProgressUnavailableMessage: 'Search progress unavailable',
      endTimeColumnLabel: 'End time',
      eventTypeColumnLabel: 'Event type',
      statusColumnLabel: 'Status',
      beforeSyncUserCountColumnLabel: 'Before sync user count',
      usersAddedColumnLabel: 'Added',
      usersRemovedColumnLabel: 'Removed',
      afterSyncUserCountColumnLabel: 'After sync user count',
      collapseRowAriaLabel: 'Collapse row',
      expandRowAriaLabel: 'Expand row',
      emptyValuePlaceholder: '-',
      takeAction: 'Take action',
      reviewAndTakeAction: 'Review and take action',
      syncPausedUntilReviewed: 'Sync paused until reviewed',
      changesAppliedSuccess: 'Changes applied',
      syncPausedSuccess: 'Sync paused',
      userCurrentlyInGroupMessage: 'This user is currently part of the membership.',
      userNotInGroupMessage: 'This user is not currently part of the membership.',
      syncHistoryRetentionNote: 'Sync history is only retained for 30 days.',
      syncHistoryRetentionNoteLabel: 'Note:',
      userManuallyAddedNote: 'Someone must have manually added this user.',
      userManuallyRemovedNote: 'Someone must have manually removed this user.',
      userAddedInSyncAriaLabel: '{0} (user was added in this sync)',
      userRemovedInSyncAriaLabel: '{0} (user was removed in this sync)',
      runIdColumnLabel: 'Run ID',
      changedByColumnLabel: 'Changed by',
      businessJustification: 'Business justification',
      openQuery: 'Open query',
      changeDetailsColumnLabel: 'Change details',
      viewDetails: 'View details',
      changeTimeColumnLabel: 'Change time',
      changeReasonColumnLabel: 'Change reason',
      onboardingRequest: 'Onboarding request',
      onboardingAutoApproved: 'Onboarding auto approved',
      statusUpdate: 'Status update',
      update: 'Update',
      submissionApproved: 'Submission approved',
      submissionRejected: 'Rejected',
      groupSettings: 'Group settings',
      ignoreThresholdOnce: 'Ignore threshold once',
      thresholdExceededApproved: 'Threshold exceeded - approved',
      notificationResolved: 'Notification resolved.',
      downloadAriaLabel: 'Download {0}',
      downloadingText: 'Downloading',
      downloadLinkText: 'Download',
      downloadPendingUsersLinkText: 'Download pending users',
      downloadError: 'Download error',
      pendingMarkerLabel: 'Pending',
      pendingMarkerAriaLabel: 'Pending — awaiting owner approval',
      resolveError: 'Resolve error',
      adfRunIdColumnLabel: 'ADF Run ID',
      takeActionDisabledTooltip: 'A message has been added for this run. No action is needed.',
      ThresholdExceededActionDialog: {
        title: 'Threshold exceeded',
      },
    },
  },
} as const;

const mockDispatch = vi.fn();
const mockUseSelector = vi.fn();
const fetchJobChangesMock = vi.fn((payload) => ({ __type: 'fetchJobChanges', payload }));
const fetchSyncJobHistoryMock = vi.fn((payload) => ({ __type: 'fetchSyncJobHistory', payload }));
const searchSyncHistoryByUserMock = vi.fn((payload) => ({ __type: 'searchSyncHistoryByUser', payload }));
const downloadMembershipChangesMock = vi.fn((payload) => ({ __type: 'downloadMembershipChanges', payload }));
const fetchThresholdNotificationMock = vi.fn((payload) => ({ __type: 'fetchThresholdNotification', payload }));
const resolveNotificationMock = vi.fn((payload) => ({ __type: 'resolveNotification', payload }));
const getPeoplePickerSuggestionsMock = vi.fn((payload) => ({ __type: 'getPeoplePickerSuggestions', payload }));
const setSelectedJobEnabledMock = vi.fn((payload) => ({ type: 'jobs/setSelectedJobEnabled', payload }));

let mockState: any;
let mockSyncHistoryItems: SyncJobHistory[];
let mockSearchResult: any;
let mockSelectedPersona: any;

vi.mock('../../store/hooks', () => ({
  useStrings: () => strings,
}));

vi.mock('react-redux', () => ({
  useDispatch: () => mockDispatch,
  useSelector: (selector: (state: unknown) => unknown) => mockUseSelector(selector),
}));

vi.mock('@fluentui/react', async () => {
  const React = await import('react');

  const classNamesFunction = () => () =>
    new Proxy(
      {},
      {
        get: (_target, prop) => String(prop),
      }
    );

  const Panel = ({ isOpen, children }: any) => (isOpen ? <div>{children}</div> : null);
  const Pivot = ({ children }: any) => <div>{children}</div>;
  const PivotItem = ({ children, headerText }: any) => (
    <section aria-label={headerText}>{children}</section>
  );
  const Label = ({ children, className }: any) => <label className={className}>{children}</label>;
  const Spinner = ({ label }: any) => <div data-testid="spinner">{label}</div>;
  const MessageBar = ({ children, onDismiss }: any) => (
    <div>
      {children}
      {onDismiss && (
        <button
          type="button"
          data-testid="message-bar-dismiss"
          onClick={onDismiss}
        />
      )}
    </div>
  );
  const Dropdown = ({ ariaLabel, selectedKey, options, onChange, onRenderTitle }: any) => {
    const selectedOption = options.find((item: any) => item.key === selectedKey);
    return (
      <div>
        {onRenderTitle?.([selectedOption])}
        <select
          aria-label={ariaLabel}
          value={selectedKey}
          onChange={(event) => {
            const option = options.find((item: any) => String(item.key) === event.target.value);
            onChange?.(event, option);
          }}
        >
          {options.map((option: any) => (
            <option key={option.key} value={option.key}>
              {option.text}
            </option>
          ))}
        </select>
      </div>
    );
  };
  const TextField = ({ ariaLabel, value, onChange, readOnly, multiline }: any) =>
    multiline ? (
      <textarea aria-label={ariaLabel} value={value} onChange={(event) => onChange?.(event, event.target.value)} readOnly={readOnly} />
    ) : (
      <input aria-label={ariaLabel} value={value} onChange={(event) => onChange?.(event, event.target.value)} readOnly={readOnly} />
    );
  const IconButton = ({ ariaLabel, title, onClick, disabled }: any) => (
    <button aria-label={ariaLabel} title={title} onClick={onClick} disabled={disabled} type="button" />
  );
  const Icon = ({ iconName, className, style }: any) => (
    <span data-icon-name={iconName} className={className} style={style} />
  );
  const Link = ({ children, onClick, disabled, 'aria-label': ariaLabel }: any) => (
    <button type="button" onClick={onClick} disabled={disabled} aria-label={ariaLabel}>
      {children}
    </button>
  );
  const DefaultButton = ({ text, onClick, disabled, 'aria-label': ariaLabel }: any) => (
    <button type="button" onClick={onClick} disabled={disabled} aria-label={ariaLabel}>
      {text}
    </button>
  );
  const PrimaryButton = ({ text, onClick, disabled, 'aria-label': ariaLabel }: any) => (
    <button type="button" onClick={onClick} disabled={disabled} aria-label={ariaLabel}>
      {text}
    </button>
  );
  const Modal = ({ isOpen, children }: any) => (isOpen ? <div>{children}</div> : null);
  const TooltipHost = ({ children, content }: any) => <div title={content}>{children}</div>;
  const DetailsRow = ({ children, item, columns }: any) => (
    <div>
      {children ?? columns?.map((column: any) => (
        <div key={column.key} data-testid={`cell-${item.id ?? item.runId}-${column.key}`}>
          {column.onRender ? column.onRender(item) : column.fieldName ? item[column.fieldName] : null}
        </div>
      ))}
    </div>
  );
  const NormalPeoplePicker = ({ onChange, ariaLabel }: any) => (
    <div>
      <button
        type="button"
        aria-label={ariaLabel}
        data-testid="people-picker-select"
        onClick={() => onChange?.(mockSelectedPersona ? [mockSelectedPersona] : [])}
      >
        Select user
      </button>
    </div>
  );
  const DetailsList = ({ items, columns, setKey, onRenderRow }: any) => (
    <div data-testid={`details-list-${setKey}`}>
      {items.map((item: any, rowIndex: number) => {
        const itemKey = item.id ?? item.runId ?? `${setKey}-${rowIndex}`;
        if (onRenderRow) {
          return (
            <div key={itemKey} data-testid={`row-${setKey}-${rowIndex}`}>
              {onRenderRow({ item, columns })}
            </div>
          );
        }

        return (
          <div key={itemKey} data-testid={`row-${setKey}-${rowIndex}`}>
            {columns.map((column: any) => (
              <div key={column.key} data-testid={`cell-${itemKey}-${column.key}`}>
                {column.onRender ? column.onRender(item) : column.fieldName ? item[column.fieldName] : null}
              </div>
            ))}
          </div>
        );
      })}
    </div>
  );

  return {
    classNamesFunction,
    DetailsList,
    DetailsListLayoutMode: { justified: 'justified' },
    DetailsRow,
    Dropdown,
    Icon,
    IconButton,
    Label,
    Link,
    DefaultButton,
    PrimaryButton,
    MessageBar,
    MessageBarType: { success: 'success', error: 'error', info: 'info' },
    Modal,
    NormalPeoplePicker,
    Panel,
    PanelType: { custom: 'custom' },
    Pivot,
    PivotItem,
    Spinner,
    SpinnerSize: { small: 'small' },
    TextField,
    TooltipHost,
    DirectionalHint: { bottomLeftEdge: 'bottomLeftEdge' },
    useTheme: () => mockTheme,
  };
});

vi.mock('../../store/jobDetails.api', () => ({
  downloadMembershipChanges: (...args: any[]) => downloadMembershipChangesMock(...args),
  fetchJobChanges: (...args: any[]) => fetchJobChangesMock(...args),
  fetchSyncJobHistory: (...args: any[]) => fetchSyncJobHistoryMock(...args),
  fetchThresholdNotification: (...args: any[]) => fetchThresholdNotificationMock(...args),
  resolveNotification: (...args: any[]) => resolveNotificationMock(...args),
  searchSyncHistoryByUser: (...args: any[]) => searchSyncHistoryByUserMock(...args),
}));

vi.mock('../../store/jobs.api', () => ({
  getPeoplePickerSuggestions: (...args: any[]) => getPeoplePickerSuggestionsMock(...args),
}));

vi.mock('../../store/jobs.slice', () => ({
  selectSelectedJobChanges: (state: any) => state.jobs.selectedJobChanges,
  selectSelectedJobDetails: (state: any) => state.jobs.selectedJobDetails,
  setSelectedJobEnabled: (...args: any[]) => setSelectedJobEnabledMock(...args),
}));

vi.mock('../../store/roles.slice', () => ({
  selectIsJobTenantReader: (state: any) => state.roles.isJobTenantReader,
  selectIsJobTenantWriter: (state: any) => state.roles.isJobTenantWriter,
  selectIsJobWriter: (state: any) => state.roles.isJobOwnerWriter || state.roles.isJobTenantWriter,
  selectIsGeneralSettingsAdministrator: (state: any) => state.roles.isGeneralSettingsAdministrator,
}));

vi.mock('../../services/signalR/SignalRSyncHistorySearchService', () => ({
  SignalRSyncHistorySearchService: class {
    onProgress: ((update: unknown) => void) | null = null;
    startConnection = vi.fn().mockResolvedValue(undefined);
    subscribe = vi.fn().mockResolvedValue(undefined);
    unsubscribe = vi.fn().mockResolvedValue(undefined);
    stopConnection = vi.fn();
  },
}));

vi.mock('../ThresholdExceededActionDialog', () => ({
  ThresholdExceededActionDialog: () => null,
}));

vi.mock('../MembershipLookup', () => ({
  MembershipLookup: () => null,
}));

const buildSyncHistoryItem = (
  runId: string,
  endTime: string,
  usersAdded: number,
  usersRemoved: number
): SyncJobHistory => ({
  runId,
  startTime: endTime,
  endTime,
  duration: 10,
  status: RunHistoryStatus.Idle,
  beforeSyncUserCount: 10,
  usersAdded,
  usersRemoved,
  afterSyncUserCount: 10 + usersAdded - usersRemoved,
  thresholdViolations: 0,
  updatedByFunction: 'Function',
  createdAt: endTime,
  updatedAt: endTime,
});

const defaultProps = {
  isOpen: true,
  dismissPanel: vi.fn(),
  jobId: 'job-1',
};

const renderPanel = async () => {
  await act(async () => {
    render(<JobHistoryPanelBase {...defaultProps} />);
    await Promise.resolve();
  });
};

const selectUser = async () => {
  await act(async () => {
    fireEvent.click(screen.getByTestId('people-picker-select'));
    await Promise.resolve();
  });

  await waitFor(() => {
    expect(searchSyncHistoryByUserMock).toHaveBeenCalled();
  });
};

beforeEach(() => {
  vi.clearAllMocks();
  mockState = {
    jobs: {
      selectedJobChanges: [],
      selectedJobDetails: {
        targetGroupId: 'target-group-id',
        targetGroupName: 'Target Group',
      },
    },
    roles: {
      isJobOwnerReader: false,
      isJobOwnerWriter: false,
      isJobTenantReader: true,
      isJobTenantWriter: true,
    },
    settings: {
      settings: [
        { settingKey: 16, settingValue: 'true' },
        { settingKey: SettingKey.RunHistoryOpenViewingAndUnifiedTab, settingValue: 'false' },
      ],
    },
  };
  mockSyncHistoryItems = [
    buildSyncHistoryItem('run-added', '2024-05-02T00:00:00Z', 3, 0),
    buildSyncHistoryItem('run-removed', '2024-05-01T00:00:00Z', 0, 2),
  ];
  mockSearchResult = {
    matchingRunIds: ['run-added'],
    runMembershipChanges: [
      {
        runId: 'run-added',
        membershipChangeType: MembershipChangeType.Added,
      },
    ],
    userInCurrentGroup: true,
    checkedCurrentGroupMembership: true,
  };
  mockSelectedPersona = {
    id: 'user-1',
    key: 'user-1',
    text: 'Test User',
    secondaryText: 'test@example.com',
  };

  mockUseSelector.mockImplementation((selector: (state: unknown) => unknown) => selector(mockState));
  mockDispatch.mockImplementation((action: any) => {
    switch (action?.__type) {
      case 'fetchSyncJobHistory':
        return { unwrap: () => Promise.resolve(mockSyncHistoryItems) };
      case 'searchSyncHistoryByUser':
        return { unwrap: () => Promise.resolve(mockSearchResult) };
      case 'getPeoplePickerSuggestions':
        return {
          unwrap: () =>
            Promise.resolve([
              { id: mockSelectedPersona.id, text: mockSelectedPersona.text, secondaryText: mockSelectedPersona.secondaryText },
            ]),
        };
      case 'fetchJobChanges':
      case 'fetchThresholdNotification':
      case 'resolveNotification':
      case 'downloadMembershipChanges':
      default:
        return { unwrap: () => Promise.resolve([]) };
    }
  });

  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    callback(0);
    return 0;
  });
});

describe('JobHistoryPanelBase event type filter', () => {
  it('offers only the combined and all-time configuration options', async () => {
    await renderPanel();

    const eventTypeFilter = screen.getByRole('combobox', {
      name: strings.JobDetails.Panel.eventTypeFilterLabel,
    });
    const options = within(eventTypeFilter).getAllByRole('option');

    expect(options).toHaveLength(2);
    expect(options[0]).toHaveTextContent(
      strings.JobDetails.Panel.eventTypeAllOption
    );
    expect(
      screen.getByText(strings.JobDetails.Panel.eventTypeAllSelectedOption)
    ).toBeInTheDocument();
    expect(options[1]).toHaveTextContent(
      strings.JobDetails.Panel.eventTypeConfigurationOption
    );
    expect(
      within(eventTypeFilter).queryByRole('option', { name: 'Sync only' })
    ).not.toBeInTheDocument();
  });
});

describe('JobHistoryPanelBase Phase 2 rollout', () => {
  it('keeps the role gate and Configuration tab while the flag is off', async () => {
    mockState.roles.isJobTenantReader = false;
    mockState.roles.isJobTenantWriter = false;
    mockState.roles.isJobOwnerReader = true;

    await renderPanel();

    expect(screen.getByRole('region', {
      name: strings.JobDetails.Panel.configurationPivotHeader,
    })).toBeInTheDocument();
    expect(screen.queryByTestId('details-list-combinedSyncSet')).not.toBeInTheDocument();
    expect(fetchSyncJobHistoryMock).not.toHaveBeenCalled();
  });

  it('opens the unified history and removes the Configuration tab while the flag is on', async () => {
    mockState.roles.isJobTenantReader = false;
    mockState.roles.isJobTenantWriter = false;
    mockState.roles.isJobOwnerReader = true;
    mockState.settings.settings = [
      { settingKey: SettingKey.IsAISearchForUserEnabled, settingValue: 'true' },
      { settingKey: SettingKey.RunHistoryOpenViewingAndUnifiedTab, settingValue: 'true' },
    ];
    mockState.jobs.selectedJobChanges = [{
      changeTime: '2024-05-03T00:00:00Z',
      changedByDisplayName: 'Test Owner',
      changedByObjectId: 'owner-1',
      changedOnBehalfOfDisplayName: null,
      changedOnBehalfOfObjectId: null,
      changeReason: SyncJobChangeReason.Update,
      changeSource: 'WebUI',
      changeDetails: '{"someDetail":"preserved"}',
      businessJustification: null,
    }];

    await renderPanel();

    expect(screen.queryByRole('region', {
      name: strings.JobDetails.Panel.configurationPivotHeader,
    })).not.toBeInTheDocument();
    expect(await screen.findByTestId('details-list-combinedSyncSet')).toBeInTheDocument();
    expect(await screen.findByTestId(
      'cell-configuration-0-2024-05-03T00:00:00Z-status'
    )).toBeInTheDocument();
    const configurationRow = await screen.findByTestId('row-combinedSyncSet-0');
    fireEvent.click(within(configurationRow).getByLabelText(strings.JobDetails.Panel.expandRowAriaLabel));
    fireEvent.click(within(configurationRow).getByText(strings.JobDetails.Panel.viewDetails));
    const detailsTextArea = screen.getAllByRole('textbox')
      .find((element) => element.tagName === 'TEXTAREA');
    expect(detailsTextArea).toHaveValue('{\n  "someDetail": "preserved"\n}');
    expect(fetchSyncJobHistoryMock).toHaveBeenCalledWith('job-1');
    expect(screen.queryByLabelText(strings.JobDetails.Panel.searchUserLabel)).not.toBeInTheDocument();
  });

  it('shows a loading spinner until both history sources resolve, then reveals the unified list once', async () => {
    mockState.settings.settings = [
      { settingKey: SettingKey.RunHistoryOpenViewingAndUnifiedTab, settingValue: 'true' },
    ];

    let resolveJobChanges: (value: unknown) => void = () => undefined;
    let resolveSyncHistory: (value: unknown) => void = () => undefined;
    const pendingJobChanges = new Promise((resolve) => { resolveJobChanges = resolve; });
    const pendingSyncHistory = new Promise((resolve) => { resolveSyncHistory = resolve; });

    mockDispatch.mockImplementation((action: any) => {
      switch (action?.__type) {
        case 'fetchJobChanges':
          return { unwrap: () => pendingJobChanges };
        case 'fetchSyncJobHistory':
          return { unwrap: () => pendingSyncHistory };
        default:
          return { unwrap: () => Promise.resolve([]) };
      }
    });

    await renderPanel();

    // Both sources still pending: spinner shown, unified list withheld to avoid the flash.
    expect(screen.getByTestId('spinner')).toBeInTheDocument();
    expect(screen.queryByTestId('details-list-combinedSyncSet')).not.toBeInTheDocument();

    // Resolving only one source keeps the spinner in place.
    await act(async () => {
      resolveJobChanges([]);
      await Promise.resolve();
    });
    expect(screen.getByTestId('spinner')).toBeInTheDocument();
    expect(screen.queryByTestId('details-list-combinedSyncSet')).not.toBeInTheDocument();

    // Resolving the second source clears the spinner and renders the sorted list.
    await act(async () => {
      resolveSyncHistory(mockSyncHistoryItems);
      await Promise.resolve();
    });

    expect(await screen.findByTestId('details-list-combinedSyncSet')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByTestId('spinner')).not.toBeInTheDocument();
    });
  });

  it('keeps threshold actions and downloads hidden for a read-only viewer', async () => {
    mockState.roles.isJobTenantReader = false;
    mockState.roles.isJobTenantWriter = false;
    mockState.roles.isJobOwnerReader = true;
    mockState.settings.settings = [
      { settingKey: SettingKey.RunHistoryOpenViewingAndUnifiedTab, settingValue: 'true' },
    ];
    mockSyncHistoryItems = [{
      ...buildSyncHistoryItem('run-threshold', '2024-05-02T00:00:00Z', 3, 0),
      status: RunHistoryStatus.ThresholdExceeded,
    }];

    await renderPanel();
    fireEvent.click(await screen.findByLabelText(strings.JobDetails.Panel.expandRowAriaLabel));

    expect(screen.queryByText(strings.JobDetails.Panel.reviewAndTakeAction)).not.toBeInTheDocument();
    expect(screen.queryByText(strings.JobDetails.Panel.downloadLinkText)).not.toBeInTheDocument();
  });

  it('allows a job owner writer to resolve a threshold violation', async () => {
    mockState.roles.isJobTenantReader = false;
    mockState.roles.isJobTenantWriter = false;
    mockState.roles.isJobOwnerWriter = true;
    mockState.settings.settings = [
      { settingKey: SettingKey.RunHistoryOpenViewingAndUnifiedTab, settingValue: 'true' },
    ];
    mockSyncHistoryItems = [{
      ...buildSyncHistoryItem('run-threshold', '2024-05-02T00:00:00Z', 3, 0),
      status: RunHistoryStatus.ThresholdExceeded,
    }];

    await renderPanel();
    fireEvent.click(await screen.findByLabelText(strings.JobDetails.Panel.expandRowAriaLabel));

    expect(await screen.findByText(strings.JobDetails.Panel.reviewAndTakeAction)).toBeEnabled();
    expect(screen.queryByText(strings.JobDetails.Panel.downloadLinkText)).not.toBeInTheDocument();
  });
});

describe('JobHistoryPanelBase threshold status highlighting', () => {
  it('renders a divider below the History panel header', async () => {
    await renderPanel();

    expect(document.querySelector('.headerDivider')).toBeInTheDocument();
  });

  it('highlights the most recent unresolved threshold-exceeded run', async () => {
    mockSyncHistoryItems = [
      {
        ...buildSyncHistoryItem('run-threshold', '2024-05-02T00:00:00Z', 3, 0),
        status: RunHistoryStatus.ThresholdExceeded,
      },
    ];

    await renderPanel();

    const statusCell = await screen.findByTestId('cell-sync-run-threshold-status');
    expect(statusCell.querySelector('.statusCellThresholdExceeded')).toBeInTheDocument();
  });

  it('does not highlight a threshold-exceeded run after a newer user action resolves it', async () => {
    mockSyncHistoryItems = [
      {
        ...buildSyncHistoryItem('run-threshold', '2024-05-02T00:00:00Z', 3, 0),
        status: RunHistoryStatus.ThresholdExceeded,
      },
    ];
    mockState.jobs.selectedJobChanges = [
      {
        changeTime: '2024-05-03T00:00:00Z',
        changedByDisplayName: 'Test Owner',
        changedByObjectId: 'owner-1',
        changedOnBehalfOfDisplayName: null,
        changedOnBehalfOfObjectId: null,
        changeReason: SyncJobChangeReason.IgnoreThresholdOnce,
        changeSource: 'WebUI',
        changeDetails: null,
        businessJustification: null,
      },
    ];

    await renderPanel();

    const statusCell = await screen.findByTestId('cell-sync-run-threshold-status');
    expect(statusCell.querySelector('.statusCellThresholdExceeded')).not.toBeInTheDocument();

    const approvedStatusCell = await screen.findByTestId(
      'cell-configuration-0-2024-05-03T00:00:00Z-status'
    );
    expect(approvedStatusCell).toHaveTextContent(
      strings.JobDetails.Panel.thresholdExceededApproved
    );
    expect(
      approvedStatusCell.querySelector('.statusCellThresholdApproved')
    ).toBeInTheDocument();

    const configurationRow = screen.getByTestId('row-combinedSyncSet-0');
    fireEvent.click(
      within(configurationRow).getByLabelText(
        strings.JobDetails.Panel.expandRowAriaLabel
      )
    );

    expect(
      screen.queryByText(strings.JobDetails.Panel.notificationResolved)
    ).not.toBeInTheDocument();
    expect(screen.queryByTestId('message-bar-dismiss')).not.toBeInTheDocument();
  });

  it('stops highlighting a threshold-exceeded run but keeps its pending marker while a rules update is under review', async () => {
    mockSyncHistoryItems = [
      {
        ...buildSyncHistoryItem('run-threshold', '2024-05-02T00:00:00Z', 3, 0),
        status: RunHistoryStatus.ThresholdExceeded,
      },
    ];
    mockState.jobs.selectedJobChanges = [
      {
        changeTime: '2024-05-03T00:00:00Z',
        changedByDisplayName: 'Test Owner',
        changedByObjectId: 'owner-1',
        changedOnBehalfOfDisplayName: null,
        changedOnBehalfOfObjectId: null,
        changeReason: SyncJobChangeReason.Update,
        changeSource: 'WebUI',
        changeDetails: null,
        businessJustification: null,
      },
    ];

    await renderPanel();

    const statusCell = await screen.findByTestId('cell-sync-run-threshold-status');
    expect(statusCell.querySelector('.statusCellThresholdExceeded')).not.toBeInTheDocument();
    expect(statusCell).not.toHaveTextContent(strings.JobDetails.Panel.syncPausedUntilReviewed);

    const addedCell = await screen.findByTestId('cell-sync-run-threshold-usersAdded');
    expect(within(addedCell).getByLabelText(strings.JobDetails.Panel.pendingMarkerAriaLabel))
      .toHaveTextContent(strings.JobDetails.Panel.pendingMarkerLabel);
  });

  it('stops highlighting a threshold-exceeded run after the rules update is approved', async () => {
    mockSyncHistoryItems = [
      {
        ...buildSyncHistoryItem('run-threshold', '2024-05-02T00:00:00Z', 3, 0),
        status: RunHistoryStatus.ThresholdExceeded,
      },
    ];
    mockState.jobs.selectedJobChanges = [
      {
        changeTime: '2024-05-03T00:00:00Z',
        changedByDisplayName: 'Test Owner',
        changedByObjectId: 'owner-1',
        changedOnBehalfOfDisplayName: null,
        changedOnBehalfOfObjectId: null,
        changeReason: SyncJobChangeReason.Update,
        changeSource: 'WebUI',
        changeDetails: null,
        businessJustification: null,
      },
      {
        changeTime: '2024-05-04T00:00:00Z',
        changedByDisplayName: 'Test Reviewer',
        changedByObjectId: 'reviewer-1',
        changedOnBehalfOfDisplayName: null,
        changedOnBehalfOfObjectId: null,
        changeReason: SyncJobChangeReason.SubmissionApproved,
        changeSource: 'WebUI',
        changeDetails: null,
        businessJustification: null,
      },
    ];

    await renderPanel();

    const statusCell = await screen.findByTestId('cell-sync-run-threshold-status');
    expect(statusCell.querySelector('.statusCellThresholdExceeded')).not.toBeInTheDocument();
    expect(statusCell).not.toHaveTextContent(strings.JobDetails.Panel.syncPausedUntilReviewed);
  });

  it('keeps threshold highlighting cleared when the rules update is rejected', async () => {
    mockSyncHistoryItems = [
      {
        ...buildSyncHistoryItem('run-threshold', '2024-05-02T00:00:00Z', 3, 0),
        status: RunHistoryStatus.ThresholdExceeded,
      },
    ];
    mockState.jobs.selectedJobChanges = [
      {
        changeTime: '2024-05-03T00:00:00Z',
        changedByDisplayName: 'Test Owner',
        changedByObjectId: 'owner-1',
        changedOnBehalfOfDisplayName: null,
        changedOnBehalfOfObjectId: null,
        changeReason: SyncJobChangeReason.Update,
        changeSource: 'WebUI',
        changeDetails: null,
        businessJustification: null,
      },
      {
        changeTime: '2024-05-04T00:00:00Z',
        changedByDisplayName: 'Test Reviewer',
        changedByObjectId: 'reviewer-1',
        changedOnBehalfOfDisplayName: null,
        changedOnBehalfOfObjectId: null,
        changeReason: SyncJobChangeReason.SubmissionRejected,
        changeSource: 'WebUI',
        changeDetails: null,
        businessJustification: null,
      },
    ];

    await renderPanel();

    const statusCell = await screen.findByTestId('cell-sync-run-threshold-status');
    expect(statusCell.querySelector('.statusCellThresholdExceeded')).not.toBeInTheDocument();
    expect(statusCell).not.toHaveTextContent(strings.JobDetails.Panel.syncPausedUntilReviewed);
  });
});

describe('JobHistoryPanelBase configuration row counts', () => {
  it('displays a rejected configuration event as Rejected', async () => {
    mockSyncHistoryItems = [];
    mockState.jobs.selectedJobChanges = [
      {
        changeTime: '2024-05-03T00:00:00Z',
        changedByDisplayName: 'Test Reviewer',
        changedByObjectId: 'reviewer-1',
        changedOnBehalfOfDisplayName: null,
        changedOnBehalfOfObjectId: null,
        changeReason: SyncJobChangeReason.SubmissionRejected,
        changeSource: 'WebUI',
        changeDetails: null,
        businessJustification: null,
      },
    ];

    await renderPanel();

    expect(await screen.findByTestId('cell-configuration-0-2024-05-03T00:00:00Z-status'))
      .toHaveTextContent('Rejected');
  });

  it('leaves membership count cells blank for configuration events', async () => {
    mockSyncHistoryItems = [];
    mockState.jobs.selectedJobChanges = [
      {
        changeTime: '2024-05-03T00:00:00Z',
        changedByDisplayName: 'Test Owner',
        changedByObjectId: 'owner-1',
        changedOnBehalfOfDisplayName: null,
        changedOnBehalfOfObjectId: null,
        changeReason: SyncJobChangeReason.IgnoreThresholdOnce,
        changeSource: 'WebUI',
        changeDetails: null,
        businessJustification: null,
      },
    ];

    await renderPanel();

    const itemId = 'configuration-0-2024-05-03T00:00:00Z';
    expect(await screen.findByTestId(`cell-${itemId}-beforeSyncUserCount`)).toBeEmptyDOMElement();
    expect(screen.getByTestId(`cell-${itemId}-usersAdded`)).toBeEmptyDOMElement();
    expect(screen.getByTestId(`cell-${itemId}-usersRemoved`)).toBeEmptyDOMElement();
    expect(screen.getByTestId(`cell-${itemId}-afterSyncUserCount`)).toBeEmptyDOMElement();
  });
});

describe('JobHistoryPanelBase search banner', () => {
  it('shows currently part of membership when the user is in the group', async () => {
    await renderPanel();

    await selectUser();

    expect(
      await screen.findByText(strings.JobDetails.Panel.userCurrentlyInGroupMessage)
    ).toBeInTheDocument();
  });

  it('shows not currently part of membership when the user is not in the group', async () => {
    mockSearchResult = {
      ...mockSearchResult,
      userInCurrentGroup: false,
    };

    await renderPanel();

    await selectUser();

    expect(
      await screen.findByText(strings.JobDetails.Panel.userNotInGroupMessage)
    ).toBeInTheDocument();
  });

  it('shows warning background styling when the user is not in the group', async () => {
    mockSearchResult = {
      ...mockSearchResult,
      userInCurrentGroup: false,
    };

    await renderPanel();

    await selectUser();

    const banner = (await screen.findByText(strings.JobDetails.Panel.userNotInGroupMessage)).closest('div');
    expect(banner).toHaveStyle(`background-color: ${mockTheme.semanticColors.warningBackground}`);
  });

  it('shows the retention note whenever the banner is visible', async () => {
    await renderPanel();

    await selectUser();

    expect(await screen.findByText(strings.JobDetails.Panel.syncHistoryRetentionNoteLabel)).toBeInTheDocument();
    expect(screen.getByText(strings.JobDetails.Panel.syncHistoryRetentionNote)).toBeInTheDocument();
  });

  it('does not show a manual note when the user is in the group with no matching runs', async () => {
    mockSearchResult = {
      matchingRunIds: [],
      runMembershipChanges: [],
      userInCurrentGroup: true,
      checkedCurrentGroupMembership: true,
    };

    await renderPanel();

    await selectUser();

    expect(screen.queryByText(strings.JobDetails.Panel.userManuallyAddedNote)).not.toBeInTheDocument();
    expect(screen.queryByText(strings.JobDetails.Panel.userManuallyRemovedNote)).not.toBeInTheDocument();
  });

  it('shows the manually removed note when the user is not in the group but the last sync added them', async () => {
    mockSearchResult = {
      matchingRunIds: ['run-added'],
      runMembershipChanges: [
        {
          runId: 'run-added',
          membershipChangeType: MembershipChangeType.Added,
        },
      ],
      userInCurrentGroup: false,
      checkedCurrentGroupMembership: true,
    };

    await renderPanel();

    await selectUser();

    expect(await screen.findByText(strings.JobDetails.Panel.userManuallyRemovedNote)).toBeInTheDocument();
  });
});

describe('JobHistoryPanelBase membership change highlighting', () => {
  it('highlights the added count when the user was added in that sync', async () => {
    mockSearchResult = {
      matchingRunIds: ['run-added'],
      runMembershipChanges: [
        {
          runId: 'run-added',
          membershipChangeType: MembershipChangeType.Added,
        },
      ],
      userInCurrentGroup: true,
      checkedCurrentGroupMembership: true,
    };

    await renderPanel();

    await selectUser();

    expect(await screen.findByLabelText('3 (user was added in this sync)')).toBeInTheDocument();
  });

  it('highlights the removed count when the user was removed in that sync', async () => {
    mockSearchResult = {
      matchingRunIds: ['run-removed'],
      runMembershipChanges: [
        {
          runId: 'run-removed',
          membershipChangeType: MembershipChangeType.Removed,
        },
      ],
      userInCurrentGroup: false,
      checkedCurrentGroupMembership: true,
    };

    await renderPanel();

    await selectUser();

    expect(await screen.findByLabelText('2 (user was removed in this sync)')).toBeInTheDocument();
  });
});

describe('JobHistoryPanelBase threshold pending counts', () => {
  const buildThresholdHistoryItem = (
    runId: string,
    endTime: string,
    overrides: Partial<SyncJobHistory> = {}
  ): SyncJobHistory => ({
    ...buildSyncHistoryItem(runId, endTime, 30, 1),
    status: RunHistoryStatus.ThresholdExceeded,
    endTime: null,
    afterSyncUserCount: null,
    thresholdViolations: 1,
    ...overrides,
  });

  it('renders pending markers under added and removed counts for the most recent unresolved ThresholdExceeded run', async () => {
    mockSyncHistoryItems = [
      buildThresholdHistoryItem('run-threshold-current', '2024-06-02T00:00:00Z', {
        usersAdded: 30,
        usersRemoved: 1,
      }),
    ];

    await renderPanel();

    const addedCell = await screen.findByTestId('cell-sync-run-threshold-current-usersAdded');
    const removedCell = await screen.findByTestId('cell-sync-run-threshold-current-usersRemoved');

    expect(within(addedCell).getByText('30')).toBeInTheDocument();
    expect(within(addedCell).getByLabelText(strings.JobDetails.Panel.pendingMarkerAriaLabel)).toHaveTextContent(strings.JobDetails.Panel.pendingMarkerLabel);
    expect(within(removedCell).getByText('1')).toBeInTheDocument();
    expect(within(removedCell).getByLabelText(strings.JobDetails.Panel.pendingMarkerAriaLabel)).toHaveTextContent(strings.JobDetails.Panel.pendingMarkerLabel);
  });

  it('renders pending markers for older ThresholdExceeded rows', async () => {
    mockSyncHistoryItems = [
      buildThresholdHistoryItem('run-threshold-current', '2024-06-02T00:00:00Z'),
      buildThresholdHistoryItem('run-threshold-old', '2024-06-01T00:00:00Z'),
    ];

    await renderPanel();

    const oldAddedCell = await screen.findByTestId('cell-sync-run-threshold-old-usersAdded');

    expect(within(oldAddedCell).getByText('30')).toBeInTheDocument();
    expect(within(oldAddedCell).getByLabelText(strings.JobDetails.Panel.pendingMarkerAriaLabel))
      .toHaveTextContent(strings.JobDetails.Panel.pendingMarkerLabel);
  });

  it('does not render pending markers for non-ThresholdExceeded rows', async () => {
    mockSyncHistoryItems = [
      buildSyncHistoryItem('run-idle', '2024-06-02T00:00:00Z', 30, 1),
    ];

    await renderPanel();

    const addedCell = await screen.findByTestId('cell-sync-run-idle-usersAdded');

    expect(within(addedCell).getByText('30')).toBeInTheDocument();
    expect(within(addedCell).queryByText(strings.JobDetails.Panel.pendingMarkerLabel)).not.toBeInTheDocument();
  });

  it('renders Download pending users for tenant admins when a ThresholdExceeded row is unresolved', async () => {
    mockSyncHistoryItems = [
      buildThresholdHistoryItem('run-threshold-download', '2024-06-02T00:00:00Z', {
        usersAdded: 2,
        usersRemoved: 0,
      }),
    ];

    await renderPanel();

    fireEvent.click(await screen.findByLabelText(strings.JobDetails.Panel.expandRowAriaLabel));

    expect(await screen.findByText(strings.JobDetails.Panel.downloadPendingUsersLinkText)).toBeInTheDocument();
  });

  it('renders the standard Download link when a ThresholdExceeded row is no longer red', async () => {
    mockSyncHistoryItems = [
      buildThresholdHistoryItem('run-threshold-download', '2024-06-02T00:00:00Z', {
        usersAdded: 2,
        usersRemoved: 0,
      }),
    ];
    mockState.jobs.selectedJobChanges = [
      {
        changeTime: '2024-06-03T00:00:00Z',
        changedByDisplayName: 'Test Owner',
        changedByObjectId: 'owner-1',
        changedOnBehalfOfDisplayName: null,
        changedOnBehalfOfObjectId: null,
        changeReason: SyncJobChangeReason.Update,
        changeSource: 'WebUI',
        changeDetails: null,
        businessJustification: null,
      },
    ];

    await renderPanel();

    const statusCell = await screen.findByTestId('cell-sync-run-threshold-download-status');
    const thresholdRow = statusCell.closest('[data-testid^="row-combinedSyncSet-"]');
    expect(thresholdRow).not.toBeNull();
    fireEvent.click(within(thresholdRow as HTMLElement).getByLabelText(strings.JobDetails.Panel.expandRowAriaLabel));

    expect(await screen.findByText(strings.JobDetails.Panel.downloadLinkText)).toBeInTheDocument();
    expect(screen.queryByText(strings.JobDetails.Panel.downloadPendingUsersLinkText)).not.toBeInTheDocument();
  });

  it('does not render the Download link for non-admins when a paused ThresholdExceeded row has pending counts', async () => {
    mockState.roles.isJobTenantWriter = false;
    mockSyncHistoryItems = [
      buildThresholdHistoryItem('run-threshold-download', '2024-06-02T00:00:00Z', {
        usersAdded: 2,
        usersRemoved: 0,
      }),
    ];

    await renderPanel();

    fireEvent.click(await screen.findByLabelText(strings.JobDetails.Panel.expandRowAriaLabel));

    expect(screen.queryByText(strings.JobDetails.Panel.downloadLinkText)).not.toBeInTheDocument();
  });
});

describe('JobHistoryPanelBase custom ADF run messages', () => {
  const buildThresholdItem = (overrides: Partial<SyncJobHistory> = {}): SyncJobHistory => ({
    runId: 'run-threshold',
    startTime: '2024-06-01T00:00:00Z',
    endTime: '2024-06-01T00:05:00Z',
    duration: 300,
    status: RunHistoryStatus.ThresholdExceeded,
    beforeSyncUserCount: 100,
    usersAdded: 90,
    usersRemoved: 0,
    afterSyncUserCount: 190,
    thresholdViolations: 1,
    updatedByFunction: 'Function',
    createdAt: '2024-06-01T00:05:00Z',
    updatedAt: '2024-06-01T00:05:00Z',
    ...overrides,
  });

  it('disables the review and take action button and shows a tooltip when a custom message exists', async () => {
    mockSyncHistoryItems = [
      buildThresholdItem({
        customMessage: 'This run was identified as problematic.',
      }),
    ];

    await renderPanel();

    fireEvent.click(await screen.findByLabelText(strings.JobDetails.Panel.expandRowAriaLabel));

    const takeActionButton = await screen.findByText(strings.JobDetails.Panel.reviewAndTakeAction);
    expect(takeActionButton).toBeDisabled();
    expect(takeActionButton.closest('div')).toHaveAttribute(
      'title',
      strings.JobDetails.Panel.takeActionDisabledTooltip
    );
  });

  it('keeps the review and take action button enabled when no custom message exists', async () => {
    mockSyncHistoryItems = [buildThresholdItem()];

    await renderPanel();

    fireEvent.click(await screen.findByLabelText(strings.JobDetails.Panel.expandRowAriaLabel));

    const takeActionButton = await screen.findByText(strings.JobDetails.Panel.reviewAndTakeAction);
    expect(takeActionButton).toBeEnabled();
  });
});

describe('JobHistoryPanelBase email deep-link auto-open (FR-002)', () => {
  it('auto-opens the take-action surface once for the most recent ThresholdExceeded run when autoOpenThresholdAction is set', async () => {
    mockSyncHistoryItems = [
      {
        ...buildSyncHistoryItem('run-threshold-old', '2024-05-01T00:00:00Z', 3, 0),
        status: RunHistoryStatus.ThresholdExceeded,
      },
      {
        ...buildSyncHistoryItem('run-threshold-latest', '2024-05-02T00:00:00Z', 5, 0),
        status: RunHistoryStatus.ThresholdExceeded,
      },
    ];

    await act(async () => {
      render(<JobHistoryPanelBase {...defaultProps} autoOpenThresholdAction />);
      await Promise.resolve();
    });

    // fetchThresholdNotification fires exactly once, proving the panel auto-opened and the ref guard held.
    await waitFor(() => {
      expect(fetchThresholdNotificationMock).toHaveBeenCalledWith('job-1');
    });
    expect(fetchThresholdNotificationMock).toHaveBeenCalledTimes(1);
  });

  it('does not auto-open when no ThresholdExceeded run exists', async () => {
    await act(async () => {
      render(<JobHistoryPanelBase {...defaultProps} autoOpenThresholdAction />);
      await Promise.resolve();
    });

    await waitFor(() => {
      expect(fetchSyncJobHistoryMock).toHaveBeenCalled();
    });
    expect(fetchThresholdNotificationMock).not.toHaveBeenCalled();
  });

  it('does not auto-open when autoOpenThresholdAction is not set even if a ThresholdExceeded run exists', async () => {
    mockSyncHistoryItems = [
      {
        ...buildSyncHistoryItem('run-threshold', '2024-05-02T00:00:00Z', 3, 0),
        status: RunHistoryStatus.ThresholdExceeded,
      },
    ];

    await renderPanel();

    await waitFor(() => {
      expect(fetchSyncJobHistoryMock).toHaveBeenCalled();
    });
    expect(fetchThresholdNotificationMock).not.toHaveBeenCalled();
  });
});
