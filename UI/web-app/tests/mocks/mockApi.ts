import { Page, Route } from '@playwright/test';

type JsonValue = Record<string, unknown> | Array<unknown>;

const tinyPngBase64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9sLzY9kAAAAASUVORK5CYII=';

type SettingRecord = {
  settingKey: number;
  settingValue: string;
};

type MockJobItem = (typeof mockJobsPage)['items'][number];

const defaultSettings: SettingRecord[] = [
  { settingKey: 0, settingValue: 'https://contoso.example/dashboard' },
  { settingKey: 1, settingValue: 'https://contoso.example/outlook-warning' },
  { settingKey: 2, settingValue: 'https://contoso.example/privacy' },
  { settingKey: 3, settingValue: 'http://localhost:3000' },
  { settingKey: 4, settingValue: 'true' },
  { settingKey: 5, settingValue: 'true' },
  { settingKey: 6, settingValue: 'false' },
  { settingKey: 7, settingValue: 'false' },
  { settingKey: 8, settingValue: 'false' },
  { settingKey: 9, settingValue: 'false' },
  { settingKey: 10, settingValue: 'true' },
];

const mockSupportEmail = 'gmm-support@contoso.com';

const settingKeyByName: Record<string, number> = {
  DashboardUrl: 0,
  OutlookWarningUrl: 1,
  PrivacyPolicyUrl: 2,
  UIUrl: 3,
  CanReviewOwnSubmissions: 4,
  CreateGroupFeatureEnabled: 5,
  IsBusinessJustificationRequired: 6,
  IsDisclaimerEnabled: 7,
  IsAutoApprovalForGroupBasedSyncsEnabled: 8,
  IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled: 9,
  IsAITitleEnabled: 10,
};

function resolveSettingKey(rawKey: string): number {
  const numeric = Number(rawKey);
  if (!Number.isNaN(numeric)) {
    return numeric;
  }

  return settingKeyByName[rawKey];
}

const mockGraphUsers = [
  {
    id: 'user-adele',
    displayName: 'Adele Vance',
    givenName: 'Adele',
    surname: 'Vance',
    userPrincipalName: 'adele@contoso.com',
    mail: 'adele@contoso.com',
  },
  {
    id: 'user-alex',
    displayName: 'Alex Wilber',
    givenName: 'Alex',
    surname: 'Wilber',
    userPrincipalName: 'alex@contoso.com',
    mail: 'alex@contoso.com',
  },
  {
    id: 'user-10',
    displayName: 'User 10',
    givenName: 'User',
    surname: '10',
    userPrincipalName: 'user10@contoso.com',
    mail: 'user10@contoso.com',
  },
  {
    id: 'mock-user-id',
    displayName: 'Playwright User',
    givenName: 'Playwright',
    surname: 'User',
    userPrincipalName: 'playwright@contoso.com',
    mail: 'playwright@contoso.com',
  },
];

const mockRoles = {
  isJobOwnerReader: true,
  isJobOwnerEnabler: true,
  isJobOwnerDeleter: true,
  isJobOwnerWriter: true,
  isJobTenantReader: true,
  isJobTenantWriter: true,
  isSubmissionReviewer: true,
  isSubmissionRejector: true,
  isHyperlinkAdministrator: true,
  isCustomMembershipProviderAdministrator: true,
  isOperationsResetAdministrator: true,
  isGeneralSettingsAdministrator: true,
  isFetchingRoles: false,
};

const mockSyncQuery = JSON.stringify([
  {
    type: 'GroupMembership',
    source: 'contoso',
    exclusionary: false,
  },
]);

const mockJobsPage = {
  items: [
    {
      syncJobId: 'mockjob001',
      targetGroupId: 'group-001',
      targetChannelId: '',
      targetDestinationType: 'GroupMembership',
      targetGroupName: null,
      targetChannelName: '',
      targetGroupEmail: 'mock-group-1@contoso.com',
      startDate: '2026-01-01T00:00:00Z',
      lastSuccessfulStartTime: '2026-03-01T00:00:00Z',
      lastSuccessfulRunTime: '2026-03-01T00:03:00Z',
      query: mockSyncQuery,
      titles: [{ partId: 'part-1', name: ': All Users in contoso' }],
      actionRequired: 'Destination Group Not Found',
      enabledOrNot: true,
      status: 'DestinationGroupNotFound',
      period: 6,
      arrow: '',
      estimatedNextRunTime: '2026-03-01T06:00:00Z',
      lastModifiedTime: '2026-03-01T01:00:00Z',
      thresholdPercentageForAdditions: 20,
      thresholdPercentageForRemovals: 20,
      endpoints: [],
      requestor: 'playwright@contoso.com',
    },
    {
      syncJobId: 'mockjob002',
      targetGroupId: 'group-002',
      targetChannelId: '',
      targetDestinationType: 'GroupMembership',
      targetGroupName: 'Engineering-All',
      targetChannelName: '',
      targetGroupEmail: 'engineering-all@contoso.com',
      startDate: '2026-01-02T00:00:00Z',
      lastSuccessfulStartTime: '2026-03-01T01:00:00Z',
      lastSuccessfulRunTime: '2026-03-01T01:02:00Z',
      query: mockSyncQuery,
      titles: [{ partId: 'part-2', name: ': Exclude All Users in Marketing' }],
      actionRequired: '',
      enabledOrNot: true,
      status: 'Idle',
      period: 6,
      arrow: '',
      estimatedNextRunTime: '2026-03-01T07:00:00Z',
      lastModifiedTime: '2026-03-01T02:00:00Z',
      thresholdPercentageForAdditions: 20,
      thresholdPercentageForRemovals: 20,
      endpoints: [],
      requestor: 'playwright@contoso.com',
    },
  ],
  totalNumberOfPages: 1,
  currentPage: 1,
  pageSize: 10,
  totalItems: 2,
};

const mockSqlSource = {
  name: 'MockSqlMembershipSource',
  customLabel: 'Mock HR Source',
};

const mockSqlAttributes = [
  {
    name: 'EmployeeType',
    customLabel: 'EmployeeType',
    type: 'string',
    hasMapping: true,
    values: [],
    description: 'Employee type attribute',
    enabled: true,
  },
  {
    name: 'SupervisorInd',
    customLabel: 'SupervisorInd',
    type: 'bit',
    hasMapping: false,
    values: [],
    description: 'Supervisor indicator attribute',
    enabled: true,
  },
  {
    name: 'PayScaleStockLevelNbr',
    customLabel: 'PayScaleStockLevelNbr',
    type: 'int',
    hasMapping: false,
    values: [],
    description: 'Pay scale stock level number',
    enabled: true,
  },
  {
    name: 'CostCenterCode',
    customLabel: 'CostCenterCode',
    type: 'string',
    hasMapping: false,
    values: [],
    description: 'Cost center code attribute',
    enabled: true,
  },
  {
    name: 'department',
    customLabel: 'Department',
    type: 'string',
    hasMapping: false,
    values: [],
    description: 'Department attribute',
    enabled: true,
  },
];

const mockAttributeMappingsByAttribute: Record<string, Array<{ description: string; code: string }>> = {
  EmployeeType: [
    { description: 'FTE', code: 'FTE' },
    { description: 'Intern', code: 'Intern' },
    { description: 'Vendor', code: 'Vendor' },
  ],
};

const mockAttributeValuesByAttribute: Record<string, string[]> = {
  department: ['IT', 'HR', 'Marketing'],
  EmployeeType: ['FTE', 'Intern', 'Vendor'],
  EmployeeType_Code: ['FTE', 'Intern', 'Vendor'],
  SupervisorInd: ['Yes', 'No'],
  PayScaleStockLevelNbr: ['65', '70', '75'],
  CostCenterCode: [],
};

async function fulfillJson(route: Route, payload: JsonValue, status = 200) {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(payload),
  });
}

function getJobDetailsById(syncJobId: string) {
  const found = mockJobsPage.items.find((job) => job.syncJobId === syncJobId);
  if (found) {
    return {
      ...found,
      targetGroupName: found.targetGroupName ?? 'Mock Destination',
      targetGroupId: found.targetGroupId || 'group-001',
      lastModifiedByDisplayName: 'Playwright User',
      lastModifiedByObjectId: 'mock-user-id',
      lastModifiedOnBehalfOfDisplayName: 'playwright@contoso.com',
      lastModifiedOnBehalfOfObjectId: 'mock-user-id',
    };
  }

  return {
    ...mockJobsPage.items[0],
    syncJobId,
    targetGroupName: 'Mock Destination',
    targetGroupId: 'group-001',
    status: 'Idle',
  };
}

export async function registerMockApiRoutes(page: Page): Promise<void> {
  const settingsState = defaultSettings.map((setting) => ({ ...setting }));
  const jobsState: MockJobItem[] = mockJobsPage.items.map((job) => ({ ...job }));
  const groupNamesById = new Map<string, string>();
  let lastCreatedGroupId = 'group-created-001';
  let lastCreatedGroupName = 'pw-created-group';
  let createdGroupCounter = 1;
  let createdJobCounter = 3;
  let serviceStatus = 0;
  let resetStatusPollCount = 0;

  const getJobDetailsFromState = (syncJobId: string) => {
    const found = jobsState.find((job) => job.syncJobId === syncJobId);
    if (found) {
      return {
        ...found,
        targetGroupName: found.targetGroupName ?? 'Mock Destination',
        targetGroupId: found.targetGroupId || 'group-001',
        lastModifiedByDisplayName: 'Playwright User',
        lastModifiedByObjectId: 'mock-user-id',
        lastModifiedOnBehalfOfDisplayName: 'playwright@contoso.com',
        lastModifiedOnBehalfOfObjectId: 'mock-user-id',
      };
    }

    return getJobDetailsById(syncJobId);
  };

  await page.route('**/api/v1/**', async (route) => {
    const request = route.request();
    const method = request.method();
    const requestUrl = new URL(request.url());
    const path = requestUrl.pathname;

    const settingsPathMatch = path.match(/\/api\/v1\/settings\/?([^/]*)$/);
    const jobDetailsMatch = path.match(/\/api\/v1\/jobDetails\/job\/([^\/]+)$/);
    const groupDetailsMatch = path.match(/\/api\/v1\/jobDetails\/group\/([^\/]+)$/);
    const channelDetailsMatch = path.match(/\/api\/v1\/jobDetails\/groups\/([^\/]+)\/channels\/([^\/]+)$/);
    const jobChangesMatch = path.match(/\/api\/v1\/jobDetails\/history\/configuration\/([^\/]+)$/);
    const syncHistoryMatch = path.match(/\/api\/v1\/jobDetails\/history\/sync\/([^\/]+)$/);
    const removeGmmMatch = path.match(/\/api\/v1\/jobDetails\/([^\/]+)\/removeGmm$/);
    const groupSearchMatch = path.match(/\/api\/v1\/destinations\/searchGroups\/([^\/]+)$/);
    const teamChannelSearchMatch = path.match(/\/api\/v1\/destinations\/teams\/([^\/]+)\/searchChannels\/([^\/]+)$/);
    const groupEndpointsMatch = path.match(/\/api\/v1\/destinations\/groups\/([^\/]+)\/endpoints$/);
    const groupOwnersMatch = path.match(/\/api\/v1\/destinations\/groups\/([^\/]+)\/owners$/);
    const groupMembersMatch = path.match(/\/api\/v1\/destinations\/groups\/([^\/]+)\/group-members$/);
    const groupOnboardingMatch = path.match(/\/api\/v1\/destinations\/groups\/([^\/]+)\/onboarding-status$/);
    const channelOnboardingMatch = path.match(/\/api\/v1\/destinations\/teams\/([^\/]+)\/channel\/([^\/]+)\/onboarding-status$/);
    const settingsKeyName = settingsPathMatch?.[1] ? decodeURIComponent(settingsPathMatch[1]) : '';

    if (method === 'GET' && path.endsWith('/api/v1/roles/getAllRoles')) {
      await fulfillJson(route, mockRoles);
      return;
    }

    if (method === 'GET' && path.endsWith('/api/v1/operations/servicestatus')) {
      if (serviceStatus === 2) {
        resetStatusPollCount += 1;
        if (resetStatusPollCount >= 2) {
          serviceStatus = 0;
        }
      }
      await fulfillJson(route, { status: serviceStatus });
      return;
    }

    if (method === 'POST' && /\/api\/v1\/operations\/(Reset|Stop|Start)$/.test(path)) {
      const operation = path.split('/').pop();
      if (operation === 'Reset') {
        serviceStatus = 2;
        resetStatusPollCount = 0;
      }
      if (operation === 'Stop') {
        serviceStatus = 1;
      }
      if (operation === 'Start') {
        serviceStatus = 0;
      }
      await fulfillJson(route, { ok: true });
      return;
    }

    if (method === 'GET' && path.endsWith('/api/v1/settings/supportEmail')) {
      await route.fulfill({
        status: 200,
        contentType: 'text/plain',
        body: mockSupportEmail,
      });
      return;
    }

    if (method === 'GET' && /\/api\/v1\/settings\/?$/.test(path) && requestUrl.searchParams.get('key')) {
      const keyName = requestUrl.searchParams.get('key') || '';
      const settingKey = resolveSettingKey(keyName);
      const setting = settingsState.find((item) => item.settingKey === settingKey);
      await fulfillJson(route, setting || { settingKey, settingValue: 'false' });
      return;
    }

    if (method === 'GET' && /\/api\/v1\/settings\/?$/.test(path)) {
      await fulfillJson(route, settingsState);
      return;
    }

    if (method === 'PATCH' && settingsPathMatch && settingsKeyName) {
      const settingKey = resolveSettingKey(settingsKeyName);
      const body = request.postData() ?? '"false"';
      const parsed = JSON.parse(body);
      const normalizedValue = typeof parsed === 'string' ? parsed : String(parsed);
      const index = settingsState.findIndex((setting) => setting.settingKey === settingKey);
      if (index >= 0) {
        settingsState[index] = { ...settingsState[index], settingValue: normalizedValue };
      } else {
        settingsState.push({ settingKey, settingValue: normalizedValue });
      }
      await fulfillJson(route, { settingKey, settingValue: normalizedValue });
      return;
    }

    if (method === 'GET' && path.endsWith('/api/v1/sqlMembershipSources/default')) {
      await fulfillJson(route, mockSqlSource);
      return;
    }

    if (method === 'GET' && path.endsWith('/api/v1/sqlMembershipSources/defaultAttributes')) {
      await fulfillJson(route, mockSqlAttributes);
      return;
    }

    if (method === 'GET' && /\/api\/v1\/jobs\/?$/.test(path)) {
      await fulfillJson(route, {
        ...mockJobsPage,
        items: jobsState,
        totalItems: jobsState.length,
      });
      return;
    }

    if (method === 'POST' && /\/api\/v1\/jobs\/?$/.test(path)) {
      const body = (request.postDataJSON() as Record<string, unknown>) ?? {};
      const syncJobId = `mockjob${String(createdJobCounter).padStart(3, '0')}`;
      createdJobCounter += 1;

      const destination = (body['destination'] as string) || lastCreatedGroupId;
      const targetGroupName = groupNamesById.get(destination) || lastCreatedGroupName;
      const now = new Date().toISOString();
      const queryValue = typeof body['query'] === 'string' ? (body['query'] as string) : JSON.stringify(body['query'] ?? []);

      jobsState.unshift({
        syncJobId,
        targetGroupId: destination,
        targetChannelId: '',
        targetDestinationType: 'GroupMembership',
        targetGroupName,
        targetChannelName: '',
        targetGroupEmail: `${targetGroupName.toLowerCase().replace(/\s+/g, '-') || 'mock-group'}@contoso.com`,
        startDate: now,
        lastSuccessfulStartTime: now,
        lastSuccessfulRunTime: now,
        query: queryValue,
        titles: [{ partId: `part-${syncJobId}`, name: ': Mock HR Query' }],
        actionRequired: '',
        enabledOrNot: true,
        status: 'PendingReview',
        period: Number(body['period']) || 6,
        arrow: '',
        estimatedNextRunTime: now,
        lastModifiedTime: now,
        thresholdPercentageForAdditions: Number(body['thresholdPercentageForAdditions']) || 20,
        thresholdPercentageForRemovals: Number(body['thresholdPercentageForRemovals']) || 20,
        endpoints: [],
        requestor: (body['requestor'] as string) || 'playwright@contoso.com',
      });

      await fulfillJson(route, { ok: true, status: 200, responseData: syncJobId }, 200);
      return;
    }

    if (method === 'POST' && path.endsWith('/api/v1/jobs/bulkDownload')) {
      await fulfillJson(route, jobsState);
      return;
    }

    if (method === 'POST' && path.endsWith('/api/v1/jobs/bulkApprove')) {
      await fulfillJson(route, { approvedJobsCount: 1 });
      return;
    }

    if (method === 'POST' && path.endsWith('/api/v1/OpenAI/generateTitle')) {
      await route.fulfill({ status: 200, contentType: 'text/plain', body: 'Mock generated title' });
      return;
    }

    if (method === 'POST' && path.endsWith('/api/v1/OpenAI/generateTitles')) {
      const parts = (request.postDataJSON() as Array<Record<string, unknown>>) || [];
      const generated = parts.map((part, index) => ({
        ...part,
        title: index % 2 === 0 ? ': All Users in Sales' : ': Exclude Everyone in John Doe\'s org',
      }));
      await fulfillJson(route, generated);
      return;
    }

    if (method === 'GET' && jobDetailsMatch) {
      const syncJobId = decodeURIComponent(jobDetailsMatch[1]);
      await fulfillJson(route, getJobDetailsFromState(syncJobId));
      return;
    }

    if (method === 'GET' && groupDetailsMatch) {
      const groupId = decodeURIComponent(groupDetailsMatch[1]);
      const found = jobsState.find((job) => job.targetGroupId === groupId);
      await fulfillJson(route, found ? getJobDetailsFromState(found.syncJobId) : getJobDetailsById('mockjob002'));
      return;
    }

    if (method === 'GET' && channelDetailsMatch) {
      await fulfillJson(route, {
        ...getJobDetailsById('mockjob002'),
        targetDestinationType: 'TeamsChannelMembership',
        targetChannelId: 'channel-001',
        targetChannelName: 'General',
      });
      return;
    }

    if (method === 'PATCH' && /\/api\/v1\/jobDetails\/[^\/]+\/(review|enable|update)$/.test(path)) {
      await fulfillJson(route, { ok: true, status: 200, responseData: 'updated' });
      return;
    }

    if (method === 'GET' && jobChangesMatch) {
      await fulfillJson(route, {
        items: [
          {
            timestamp: '2026-03-01T00:00:00Z',
            actor: 'Playwright User',
            changeType: 'Update',
            details: 'Mock change details',
          },
        ],
      });
      return;
    }

    if (method === 'GET' && syncHistoryMatch) {
      await fulfillJson(route, [
        {
          runId: 'run-001',
          status: 'Idle',
          startTime: '2026-03-01T00:00:00Z',
          endTime: '2026-03-01T00:01:00Z',
          usersAdded: 2,
          usersRemoved: 1,
        },
      ]);
      return;
    }

    if (method === 'POST' && removeGmmMatch) {
      await fulfillJson(route, { ok: true, statusCode: 200 });
      return;
    }

    if (method === 'GET' && groupSearchMatch) {
      const query = decodeURIComponent(groupSearchMatch[1]).toLowerCase();
      const data = [
        {
          id: 'group-001',
          name: 'contoso',
          email: 'contoso@contoso.com',
          endpoints: ['group-001@contoso.com'],
        },
        {
          id: 'group-002',
          name: 'engineering-all',
          email: 'engineering-all@contoso.com',
          endpoints: ['group-002@contoso.com'],
        },
      ].filter((group) => group.name.toLowerCase().includes(query) || group.email.toLowerCase().includes(query));
      await fulfillJson(route, data);
      return;
    }

    if (method === 'POST' && path.endsWith('/api/v1/destinations/groups')) {
      const body = (request.postDataJSON() as Record<string, unknown>) ?? {};
      const groupName = (body['groupName'] as string) || `pw-created-group-${createdGroupCounter}`;
      const groupId = `group-created-${String(createdGroupCounter).padStart(3, '0')}`;
      createdGroupCounter += 1;
      lastCreatedGroupId = groupId;
      lastCreatedGroupName = groupName;
      groupNamesById.set(groupId, groupName);

      await fulfillJson(route, {
        groupId,
        groupName,
        errorCode: null,
      });
      return;
    }

    if (method === 'GET' && groupEndpointsMatch) {
      await fulfillJson(route, ['group@contoso.com']);
      return;
    }

    if (method === 'GET' && groupOwnersMatch) {
      await fulfillJson(route, [
        {
          objectId: 'mock-user-id',
          displayName: 'Playwright User',
          mail: 'playwright@contoso.com',
          userPrincipalName: 'playwright@contoso.com',
        },
      ]);
      return;
    }

    if (method === 'GET' && groupMembersMatch) {
      await fulfillJson(route, {
        groupId: 'group-001',
        groupMemberCount: 0,
        groups: [],
      });
      return;
    }

    if (method === 'GET' && groupOnboardingMatch) {
      await fulfillJson(route, {
        status: 'Onboarded',
      });
      return;
    }

    if (method === 'GET' && teamChannelSearchMatch) {
      await fulfillJson(route, [
        {
          channelId: 'channel-001',
          name: 'General',
        },
      ]);
      return;
    }

    if (method === 'GET' && channelOnboardingMatch) {
      await fulfillJson(route, {
        status: 'Onboarded',
      });
      return;
    }

    if (method === 'GET' && /\/api\/v1\/orgLeaderDetails\/EmployeeId\/.+$/.test(path)) {
      await fulfillJson(route, {
        azureObjectId: 'user-10',
        maxDepth: 3,
      });
      return;
    }

    if (method === 'GET' && /\/api\/v1\/orgLeaderDetails\/ObjectId\/.+$/.test(path)) {
      await fulfillJson(route, {
        employeeId: 10,
        maxDepth: 3,
      });
      return;
    }

    const attributeValuesMatch = path.match(/\/api\/v1\/sqlMembershipSources\/attributeValues\/([^\/]+)$/);
    if (method === 'GET' && attributeValuesMatch) {
      const attributeName = decodeURIComponent(attributeValuesMatch[1]);
      await fulfillJson(route, mockAttributeValuesByAttribute[attributeName] ?? []);
      return;
    }

    const attributeMappingsMatch = path.match(/\/api\/v1\/sqlMembershipSources\/attributeMappings\/([^\/]+)$/);
    if (method === 'GET' && attributeMappingsMatch) {
      const attributeName = decodeURIComponent(attributeMappingsMatch[1]);
      await fulfillJson(route, mockAttributeMappingsByAttribute[attributeName] ?? []);
      return;
    }

    if (method === 'POST' && path.endsWith('/api/v1/sqlMembershipSources/validateFilters')) {
      await fulfillJson(route, { isValid: true, invalidFilters: [] });
      return;
    }

    if (method === 'PATCH' && path.endsWith('/api/v1/sqlMembershipSources/default')) {
      await fulfillJson(route, {});
      return;
    }

    if (method === 'PATCH' && path.endsWith('/api/v1/sqlMembershipSources/defaultAttributes')) {
      await fulfillJson(route, {});
      return;
    }

    if (method === 'GET') {
      await fulfillJson(route, {});
      return;
    }

    await fulfillJson(route, { ok: true, method, path });
  });

  await page.route('**/graph/v1.0/**', async (route) => {
    const requestUrl = new URL(route.request().url());
    const path = requestUrl.pathname;

    if (path.includes('/photos/48x48/$value')) {
      await route.fulfill({
        status: 200,
        contentType: 'image/png',
        body: tinyPngBase64,
      });
      return;
    }

    if (path.includes('/users/')) {
      const userId = path.split('/users/')[1]?.split('/')[0];
      const user = mockGraphUsers.find((item) => item.id === userId) || mockGraphUsers[3];
      await fulfillJson(route, {
        id: user.id,
        displayName: user.displayName,
        preferredLanguage: 'en',
        userPrincipalName: user.userPrincipalName,
        mail: user.mail,
      });
      return;
    }

    if (path.endsWith('/users')) {
      const rawSearch = (requestUrl.searchParams.get('$search') || '').toLowerCase();
      const rawFilter = (requestUrl.searchParams.get('$filter') || '').toLowerCase();
      // Extract search terms from OData expressions like "mail:adele" or startswith(displayName,'adele')
      const extractTerms = (s: string): string[] => {
        const terms: string[] = [];
        // Match quoted field:value patterns from $search (e.g., "mail:adele")
        for (const m of s.matchAll(/"(?:\w+:)?([^"]+)"/g)) terms.push(m[1]);
        // Match startswith(field,'value') patterns from $filter
        for (const m of s.matchAll(/startswith\(\w+,'([^']+)'\)/g)) terms.push(m[1]);
        // Fallback: use the raw string if no patterns matched
        if (terms.length === 0 && s.trim()) terms.push(s.trim());
        return terms;
      };
      const searchTerms = [...extractTerms(rawSearch), ...extractTerms(rawFilter)];
      const value = mockGraphUsers.filter((user) =>
        searchTerms.length === 0 || searchTerms.some((term) =>
          user.displayName.toLowerCase().includes(term) ||
          user.mail.toLowerCase().includes(term) ||
          user.userPrincipalName.toLowerCase().includes(term)
        )
      );

      await fulfillJson(route, { value: value.length > 0 ? value : mockGraphUsers });
      return;
    }

    await fulfillJson(route, { value: [] });
  });
}
