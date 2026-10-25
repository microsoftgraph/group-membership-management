// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { classNamesFunction, Toggle, IProcessedStyleSet, Pivot, PivotItem, PrimaryButton, DefaultButton, TextField, Text, TooltipHost, IColumn, SelectionMode, ShimmeredDetailsList, Dropdown, Spinner, IRenderFunction, ISelectableDroppableTextProps, IDropdown, Slider, Icon, IconButton, ActionButton, Link, MessageBar, MessageBarType, mergeStyles } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import type { ITheme } from '@fluentui/react/lib/Theme';
import { VerticalStackedBarChart } from '@fluentui/react-charting';
import type { IVerticalStackedChartProps, IVSChartDataPoint } from '@fluentui/react-charting';
import {
  AdminConfigStyleProps,
  AdminConfigStyles,
  AdminConfigViewProps,
  CustomLabelCellProps,
  NullThresholdCellProps,
  AttributeValuesCellProps,
  CustomSourceSettingsProps,
  UserResourcesSettingsProps,
  OperationsProps,
  GeneralSettingsProps,
  AutoApproverSettingsProps,
  SettingsSectionProps,
  DashboardProps,
  AISettingsProps } from './AdminConfig.types';
import { PageSection } from '../../components/PageSection';
import { HyperlinkSetting } from '../../components/HyperlinkSetting';
import { Operation } from '../../components/Operation';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { SettingKey, SettingKeyMap, SqlMembershipAttribute, SqlMembershipSource } from '../../models';
import type { PendingReviewSummaryRow, InProgressStatusRow, AnomalousStatusRow } from '../../models/DashboardSummary';
import type { ReviewerActivityRow } from '../../models/ApprovalActivity';
import type { SubmitterActivityRow } from '../../models/OnBehalfOfActivity';
import { GeneralSetting } from '../../components/GeneralSetting';
import { ServiceNotificationSettings, validateServiceNotification } from './ServiceNotificationSettings';
import { ConfiguredAttributeRules, ConfiguredAttributeToggleCard, useConfiguredAttributeApproval } from './ConfiguredAttributeApprovalSettings';
import { useAppDispatch, useAppSelector } from '../../store/hooks';
import { fetchApprovalActivity, fetchDashboardSummary, fetchOnBehalfOfActivity } from '../../store/dashboard.api';
import {
  selectDashboardSummary,
  selectDashboardLoading,
  selectDashboardError,
  selectApprovalActivity,
  selectApprovalActivityLoading,
  selectApprovalActivityError,
  selectApprovalWindowDays,
  setApprovalWindowDays,
  selectOnBehalfOfActivity,
  selectOnBehalfOfActivityLoading,
  selectOnBehalfOfActivityError,
  selectOnBehalfOfWindowDays,
  setOnBehalfOfWindowDays } from '../../store/dashboard.slice';
import type { AlertBannerConfig } from '../../models/AlertBannerConfig';
import { getAccessibleStatusColors, isDarkTheme } from '../../theme/accessibleColors';

const getClassNames = classNamesFunction<AdminConfigStyleProps, AdminConfigStyles>();

export const AdminConfigView: React.FunctionComponent<AdminConfigViewProps> = (props: AdminConfigViewProps) => {
  // extract props
  const { className, isSaving, onSave, handleGetValues, settings, sqlMembershipSource, sqlMembershipSourceAttributes, strings, styles,
    serviceNotification,
    serviceNotificationSaveError,
    isCustomMembershipProviderAdmin,
    isOperationsResetAdministrator,
    isGeneralSettingsAdministrator,
    isAutoApproverAdministrator,
    isAISettingsAdministrator,
    canViewGeneralSettings,
    canViewAutoApproverSettings,
    canViewAISettings,
    canViewCustomSourceSettings,
    canViewDashboard,
    isReadOnly,
    defaultAIPrompt } = props;

  // generate class names
  const classNames: IProcessedStyleSet<AdminConfigStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  // setup the ui state
  const [newSettings, setNewSettings] = useState(settings);
  const [newSource, setNewSource] = useState<SqlMembershipSource | undefined>(sqlMembershipSource);
  const [newAttributes, setNewAttributes] = useState<SqlMembershipAttribute[] | undefined>(sqlMembershipSourceAttributes);
  const [hasUrlValidationErrors, setHasUrlValidationErrors] = useState<boolean>(false);
  const [hasAttributeValidationErrors, setHasAttributeValidationErrors] = useState<boolean>(false);
  const [newServiceNotification, setNewServiceNotification] = useState<AlertBannerConfig>(serviceNotification);
  const [selectedTabKey, setSelectedTabKey] = useState<string | undefined>(undefined);
  const configured = useConfiguredAttributeApproval(strings.AutoApproverSettings.labels, isAutoApproverAdministrator, canViewAutoApproverSettings);

  const visibleTabKeys = useMemo(() => [
    ...(canViewGeneralSettings ? ['general'] : []),
    ...(isOperationsResetAdministrator ? ['operations'] : []),
    ...(canViewCustomSourceSettings ? ['customSource'] : []),
    ...(canViewAISettings ? ['ai'] : []),
    ...(canViewAutoApproverSettings ? ['autoApprover'] : []),
    ...(canViewDashboard ? ['dashboard'] : []),
  ], [canViewGeneralSettings, isOperationsResetAdministrator, canViewCustomSourceSettings, canViewAISettings, canViewAutoApproverSettings, canViewDashboard]);

  // Fluent locks the Pivot into uncontrolled mode if selectedKey is ever undefined, so always pass a key.
  const effectiveTabKey = selectedTabKey && visibleTabKeys.includes(selectedTabKey)
    ? selectedTabKey
    : (visibleTabKeys[0] ?? 'general');

  useEffect(() => {
    setNewServiceNotification(serviceNotification);
  }, [serviceNotification]);

  const serviceNotificationErrors = useMemo(
    () => validateServiceNotification(newServiceNotification, strings),
    [newServiceNotification, strings]
  );
  const hasServiceNotificationErrors = Object.keys(serviceNotificationErrors).length > 0;

  useEffect(() => {
    setNewSource(sqlMembershipSource);
  }, [sqlMembershipSource]);

  useEffect(() => {
    setNewAttributes(sqlMembershipSourceAttributes);
  }, [sqlMembershipSourceAttributes]);

  useEffect(() => {
    setNewSettings(settings);
  }, [settings]);

  // create state helpers
  const hasChanges = () => {
    const hasSettingsChanges = Object.entries(newSettings).some(([key, value]) => value !== settings[Number(key) as SettingKey]);
    const hasSourceChanges = JSON.stringify(newSource) !== JSON.stringify(sqlMembershipSource);
    const hasAttributesChanges = JSON.stringify(newAttributes) !== JSON.stringify(sqlMembershipSourceAttributes);
    const hasServiceNotificationChanges = JSON.stringify(newServiceNotification) !== JSON.stringify(serviceNotification);
    return hasSettingsChanges || hasSourceChanges || hasAttributesChanges || hasServiceNotificationChanges;
  };

  // setup ui event handlers
  const handleOnSaveButtonClick = () => {
    if (configured.hasIncompleteRows) {
      configured.revealErrors();
      return;
    }
    if (hasChanges()) onSave(newSettings, newSource, newAttributes, newServiceNotification);
    if (configured.isDirty) void configured.save();
  };

  return (
    <Page>
      <PageHeader />
      <div className={classNames.root}>
        <div className={classNames.titleRow}>
          <div className={classNames.title}>{strings.labels.pageTitle}</div>
          <PrimaryButton
            text={strings.labels.saveButton}
            onClick={handleOnSaveButtonClick}
            className={classNames.saveButton}
            title={isReadOnly ? strings.labels.readOnlyTooltip : undefined}
            disabled={isReadOnly || !(hasChanges() || configured.isDirty || configured.hasIncompleteRows) || hasUrlValidationErrors || hasAttributeValidationErrors || hasServiceNotificationErrors || configured.hasNoRules || isSaving}
          ></PrimaryButton>
        </div>
        {isReadOnly &&
          <MessageBar messageBarType={MessageBarType.info}>
            {strings.labels.readOnlyBanner}
          </MessageBar>
        }
        <div className={classNames.card}>
          <PageSection>
            <Pivot
              selectedKey={effectiveTabKey}
              onLinkClick={(item) => setSelectedTabKey(item?.props.itemKey)}
            >
              {canViewGeneralSettings &&
                <PivotItem
                  itemKey="general"
                  headerText={strings.GeneralSettings.labels.general}
                  headerButtonProps={{
                    'data-order': 1,
                    'data-title': strings.GeneralSettings.labels.general,
                  }}
                >
                  <GeneralSettings
                    classNames={classNames}
                    strings={strings}
                    settings={newSettings}
                    setSettings={setNewSettings}
                    setHasValidationErrors={setHasUrlValidationErrors}
                    canEdit={isGeneralSettingsAdministrator}
                    serviceNotification={newServiceNotification}
                    setServiceNotification={setNewServiceNotification}
                    serviceNotificationErrors={serviceNotificationErrors}
                    serviceNotificationSaveError={serviceNotificationSaveError} />
                </PivotItem>
              }
              {isOperationsResetAdministrator &&
                <PivotItem
                  itemKey="operations"
                  headerText={strings.Operations.labels.operations}
                  headerButtonProps={{
                    'data-order': 2,
                    'data-title': strings.Operations.labels.operations,
                  }}
                >
                  <Operations
                    classNames={classNames}
                    strings={strings} />
                </PivotItem>
              }
              {canViewCustomSourceSettings &&
                <PivotItem
                  itemKey="customSource"
                  headerText={strings.CustomSourceSettings.labels.customSource}
                  headerButtonProps={{
                    'data-order': 3,
                    'data-title': strings.CustomSourceSettings.labels.customSource,
                  }}
                >
                  <CustomSourceSettings
                    classNames={classNames}
                    sqlMembershipSource={sqlMembershipSource}
                    sqlMembershipSourceAttributes={sqlMembershipSourceAttributes}
                    setNewAttributes={setNewAttributes}
                    setNewSource={setNewSource}
                    handleGetValues={handleGetValues}
                    canEdit={isCustomMembershipProviderAdmin}
                    setHasValidationErrors={setHasAttributeValidationErrors}
                    strings={strings} />
                </PivotItem>
              }
              {canViewAISettings &&
                <PivotItem
                  itemKey="ai"
                  headerText={strings.AISettings.labels.aiSettings}
                  headerButtonProps={{
                    'data-order': 4,
                    'data-title': strings.AISettings.labels.aiSettings,
                  }}
                >
                  <AISettings
                    classNames={classNames}
                    strings={strings}
                    settings={newSettings}
                    setSettings={setNewSettings}
                    canEdit={isAISettingsAdministrator}
                    defaultAIPrompt={defaultAIPrompt} />
                </PivotItem>
              }
              {canViewAutoApproverSettings &&
                <PivotItem
                  itemKey="autoApprover"
                  headerText={strings.AutoApproverSettings.labels.autoApprover}
                  headerButtonProps={{
                    'data-order': 5,
                    'data-title': strings.AutoApproverSettings.labels.autoApprover,
                  }}
                >
                  <AutoApproverSettings
                    classNames={classNames}
                    strings={strings}
                    settings={newSettings}
                    canEdit={isAutoApproverAdministrator}
                    setSettings={setNewSettings}
                    sqlMembershipSourceAttributes={newAttributes}
                    canViewCustomSourceSettings={canViewCustomSourceSettings}
                    configured={configured}
                    onNavigateToCustomSource={() => setSelectedTabKey('customSource')} />
                </PivotItem>
              }
              {canViewDashboard &&
                <PivotItem
                  itemKey="dashboard"
                  headerText={strings.Dashboard.labels.dashboard}
                  headerButtonProps={{
                    'data-order': 6,
                    'data-title': strings.Dashboard.labels.dashboard,
                  }}
                >
                  <Dashboard
                    classNames={classNames}
                    strings={strings} />
                </PivotItem>
              }
            </Pivot>
          </PageSection>
        </div>
      </div>
    </Page>
  );
};

const SettingsSection: React.FunctionComponent<SettingsSectionProps> = (props: SettingsSectionProps) => {
  const { classNames, title, subtitle, showDivider, children } = props;
  return (
    <section className={showDivider ? classNames.sectionContainerDivided : classNames.sectionContainer}>
      <Text variant="mediumPlus" className={classNames.sectionHeading}>{title}</Text>
      {subtitle && <Text variant="small" className={classNames.sectionSubtitle}>{subtitle}</Text>}
      {children}
    </section>
  );
}

const Operations: React.FunctionComponent<OperationsProps> = (props: OperationsProps) => {
  const { classNames, strings} = props;
  return (
    <SettingsSection classNames={classNames} title={strings.Operations.labels.controlPanel}>
      <div className={classNames.operationsGrid}>
        <Operation
          variant="service"
          title={strings.Operations.labels.serviceOperationTitle}
          description={strings.Operations.labels.serviceOperationDescription}
          buttonText={strings.Operations.buttons}
        ></Operation>
        <Operation
          variant="reset"
          title={strings.Operations.labels.resetOperationTitle}
          description={strings.Operations.labels.resetOperationDescription}
          buttonText={strings.Operations.buttons}
        ></Operation>
      </div>
    </SettingsSection>
  );
}

type DashboardTone = 'good' | 'warning' | 'danger' | 'info' | 'neutral';

// The API serializes UTC timestamps; tolerate payloads that omit the zone designator
// so the browser does not reinterpret them as local time.
const parseUtc = (value?: string | null): Date | null => {
  if (!value) {
    return null;
  }
  const normalized = /(Z|[+-]\d{2}:?\d{2})$/.test(value) ? value : `${value}Z`;
  const parsed = new Date(normalized);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
};

const calendarDayDiff = (value: Date): number => {
  const now = new Date();
  const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
  const startOfValue = new Date(value.getFullYear(), value.getMonth(), value.getDate()).getTime();
  return Math.floor((startOfToday - startOfValue) / 86400000);
};

const toneColorFor = (theme: ITheme, tone: DashboardTone): string => {
  const isDarkMode = isDarkTheme(theme);
  const statusColors = getAccessibleStatusColors(theme);
  switch (tone) {
    case 'good': return isDarkMode ? statusColors.successText : theme.palette.green;
    case 'warning': return isDarkMode ? statusColors.warningIcon : theme.palette.yellowDark;
    case 'danger': return isDarkMode ? statusColors.errorText : theme.palette.redDark;
    case 'info': return statusColors.accentText;
    default: return theme.palette.neutralSecondary;
  }
};

type CountCard = { key: string; label: string; value: number; detail?: string; tone: DashboardTone };

const renderCountCards = (
  classNames: IProcessedStyleSet<AdminConfigStyles>,
  theme: ITheme,
  totalLabel: string | undefined,
  cards: CountCard[],
  headline = false
) => {
  const displayedCards: CountCard[] = totalLabel
    ? [...cards, { key: '__total__', label: totalLabel, value: cards.reduce((sum, card) => sum + card.value, 0), tone: 'neutral' }]
    : cards;

  return (
    <div className={classNames.dashboardCounts} role="list">
      {displayedCards.map((card) => (
        <div
          key={card.key}
          role="listitem"
          aria-label={`${card.label}: ${card.value.toLocaleString()}`}
          title={card.detail}
          className={headline ? mergeStyles(classNames.dashboardCount, classNames.dashboardHeadlineCount) : classNames.dashboardCount}
          style={{ borderLeftColor: toneColorFor(theme, card.tone) }}>
          <span className={classNames.dashboardCountLabel} title={card.label}>{card.label}</span>
          <span className={classNames.dashboardCountValue}>{card.value.toLocaleString()}</span>
        </div>
      ))}
    </div>
  );
};

type DashboardCardHeaderProps = Pick<DashboardProps, 'classNames'> & {
  title: string;
  description: string;
  children?: React.ReactNode;
};

const DashboardCardHeader: React.FunctionComponent<DashboardCardHeaderProps> = ({ classNames, title, description, children }) => (
  <header className={classNames.dashboardCardHeader}>
    <div className={classNames.dashboardCardTitle}>
      <Text as="h3" className={classNames.dashboardHeading}>{title}</Text>
      <TooltipHost content={description}>
        <Icon iconName="Info" tabIndex={0} aria-label={description} className={classNames.dashboardInfoIcon} />
      </TooltipHost>
    </div>
    {children}
  </header>
);

type DashboardTimeRangeProps = Pick<DashboardProps, 'strings'> & {
  windowDays: number | 'individual';
  disabled: boolean;
  ariaLabel: string;
  onChange: (windowDays: number) => void;
};

const DashboardTimeRange: React.FunctionComponent<DashboardTimeRangeProps> = ({ strings, windowDays, disabled, ariaLabel, onChange }) => {
  const labels = strings.Dashboard.labels;
  const theme = useTheme();
  const options = [
    { key: 7, text: labels.range7Days, selectedText: labels.last7Days },
    { key: 14, text: labels.range14Days, selectedText: labels.last14Days },
    { key: 30, text: labels.range1Month, selectedText: labels.last1Month },
    { key: 90, text: labels.range3Months, selectedText: labels.last3Months },
    ...(windowDays === 'individual'
      ? [{ key: 'individual', text: labels.individualRanges, selectedText: labels.individualRanges, disabled: true }]
      : []),
  ];

  return (
    <Dropdown
      ariaLabel={ariaLabel}
      selectedKey={windowDays}
      options={options}
      disabled={disabled}
      onRenderTitle={() => <span>{options.find((option) => option.key === windowDays)?.selectedText}</span>}
      onRenderCaretDown={() => <Icon iconName="Calendar" aria-hidden="true" styles={{ root: { fontSize: 10 } }} />}
      styles={{
        root: { width: 150, maxWidth: '100%' },
        title: { borderRadius: 2, borderColor: theme.palette.neutralTertiaryAlt, fontSize: 12 },
      }}
      onChange={(_event, option) => {
        if (typeof option?.key === 'number') {
          onChange(option.key);
        }
      }} />
  );
};

// Fixed series colors: chart hues must stay distinguishable from each other rather than
// track the theme. Each reviewer gets an approved/rejected pair so their rejections stack
// directly above their approvals in the same hue family.
const AUTOMATED_COLOR = '#126CC0';
const REVIEWER_HUES = [
  { approved: '#008AF4', rejected: '#86C8FF' },
  { approved: '#8B7BD8', rejected: '#54459F' },
  { approved: '#E8A33D', rejected: '#AE6E12' },
  { approved: '#4C9AFF', rejected: '#245FA8' },
  { approved: '#D96BA0', rejected: '#9E3568' },
  { approved: '#9A9896', rejected: '#63615F' },
];

// Reviewer rows arrive pre-sorted by the API with the aggregated "Other" bucket last, so the
// index-based hue assignment is stable between the chart, the legend and the reviewer table.
const reviewerHue = (index: number) => REVIEWER_HUES[index % REVIEWER_HUES.length];

// Submitter rows arrive pre-sorted by the API with the aggregated "Other" bucket last, so the
// index-based hue assignment is stable between the chart, the legend and the submitter table.
const SUBMITTER_HUES = [
  { onboarding: '#126CC0', update: '#86C8FF' },
  { onboarding: '#8B7BD8', update: '#54459F' },
  { onboarding: '#E8A33D', update: '#AE6E12' },
  { onboarding: '#4C9AFF', update: '#245FA8' },
  { onboarding: '#D96BA0', update: '#9E3568' },
  { onboarding: '#9A9896', update: '#63615F' },
];

const submitterHue = (index: number) => SUBMITTER_HUES[index % SUBMITTER_HUES.length];

// The charts plot onto a string/band axis - IVerticalStackedChartProps types xAxisPoint as
// `number | string`, so a real date axis is unavailable and d3 draws a tick for every band. That is
// why xAxisTickCount is documented as "Does not work for string axis". Thinning therefore has to
// happen in the data, by blanking the labels we do not want drawn.
// The approvals and on-behalf-of charts share a row, so each only gets about half the page width.
const MAX_SECTION_AXIS_LABELS = 10;
const AXIS_ROTATE_ABOVE = 7;
const ZERO_WIDTH_SPACE = '\u200b';

const axisStride = (total: number, maxLabels: number): number =>
  Math.max(1, Math.ceil(total / Math.max(1, maxLabels)));

// How many labels survive thinning, which is what decides whether they still need rotating.
const visibleAxisLabels = (total: number, maxLabels: number): number =>
  total === 0 ? 0 : Math.ceil(total / axisStride(total, maxLabels));

const thinAxisLabels = (data: IVerticalStackedChartProps[], maxLabels: number): IVerticalStackedChartProps[] => {
  const stride = axisStride(data.length, maxLabels);
  if (stride === 1) {
    return data;
  }
  return data.map((point, index) => (index % stride === 0 ? point : {
    ...point,
    // A blanked band still needs a unique key, otherwise the band scale collapses it onto its
    // neighbour and the two stacks draw on top of each other. Zero-width spaces are unique per
    // index and render as nothing.
    xAxisPoint: ZERO_WIDTH_SPACE.repeat(index + 1),
    // The axis no longer shows this bucket, so keep its real label reachable on hover.
    xAxisCalloutData: point.xAxisCalloutData ?? String(point.xAxisPoint)
  }));
};

const formatChartDay = (value: string): string => {
  const parsed = parseUtc(value);
  return parsed
    ? parsed.toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' })
    : '';
};

const DashboardActivityChart = ({ data, title }: { data: IVerticalStackedChartProps[]; title: string }): JSX.Element => {
  const theme = useTheme();

  return (
    <VerticalStackedBarChart
      data={data}
      height={300}
      barWidth={28}
      hideLegend={false}
      hideLabels={false}
      enabledLegendsWrapLines
      yAxisTickCount={5}
      rotateXAxisLables={visibleAxisLabels(data.length, MAX_SECTION_AXIS_LABELS) > AXIS_ROTATE_ABOVE}
      chartTitle={title}
      styles={{
        root: { width: '100%', minWidth: 0 },
        yAxis: { selectors: { '.tick line': { stroke: theme.palette.neutralTertiaryAlt }, '.domain': { display: 'none' } } },
        xAxis: { selectors: { '.domain, .tick line': { stroke: theme.palette.neutralLight } } },
        barLabel: { fontSize: 11, fontWeight: 400 },
        legendContainer: { paddingTop: 8 },
      }} />
  );
};

const ApprovalActivitySection: React.FunctionComponent<DashboardProps> = (props: DashboardProps) => {
  const { classNames, strings } = props;
  const dispatch = useAppDispatch();
  const theme = useTheme();
  const loadedActivity = useAppSelector(selectApprovalActivity);
  const loading = useAppSelector(selectApprovalActivityLoading);
  const error = useAppSelector(selectApprovalActivityError);
  const windowDays = useAppSelector(selectApprovalWindowDays);
  const activity = loadedActivity?.windowDays === windowDays ? loadedActivity : undefined;

  const labels = strings.Dashboard.labels;

  useEffect(() => {
    dispatch(fetchApprovalActivity(windowDays));
  }, [dispatch, windowDays]);

  const reviewers = useMemo(() => activity?.reviewers ?? [], [activity]);

  const chartData: IVerticalStackedChartProps[] = useMemo(() => thinAxisLabels((activity?.days ?? []).map((day) => {
    const points: IVSChartDataPoint[] = [];
    if (day.automated > 0) {
      points.push({ legend: labels.automated, data: day.automated, color: AUTOMATED_COLOR });
    }
    reviewers.forEach((reviewer, index) => {
      const hue = reviewerHue(index);
      const counts = day.reviewers.find((entry) => entry.reviewerKey === reviewer.reviewerKey);
      if (counts && counts.approved > 0) {
        points.push({ legend: reviewer.displayName, data: counts.approved, color: hue.approved });
      }
      if (counts && counts.rejected > 0) {
        points.push({ legend: `${reviewer.displayName} (${labels.rejectedSuffix})`, data: counts.rejected, color: hue.rejected });
      }
    });
    return { xAxisPoint: formatChartDay(day.date), chartData: points };
  }), MAX_SECTION_AXIS_LABELS), [activity, reviewers, labels.automated, labels.rejectedSuffix]);

  const hasData = chartData.some((day) => day.chartData.length > 0);

  const reviewerColumns: IColumn[] = [
    {
      key: 'reviewer', name: labels.reviewer, minWidth: 180, maxWidth: 320, isResizable: true,
      onRender: (item: ReviewerActivityRow, index?: number) => (
        <span className={mergeStyles({ display: 'flex', alignItems: 'center', gap: 8 })}>
          <span
            aria-hidden="true"
            className={mergeStyles({
              width: 10,
              height: 10,
              borderRadius: 2,
              flexShrink: 0,
              backgroundColor: reviewerHue(index ?? 0).approved
            })} />
          <span>{item.displayName}</span>
        </span>
      )
    },
    { key: 'approved', name: labels.approved, fieldName: 'approved', minWidth: 90, maxWidth: 120, isResizable: true },
    { key: 'rejected', name: labels.rejected, fieldName: 'rejected', minWidth: 90, maxWidth: 120, isResizable: true },
    {
      key: 'total', name: labels.total, minWidth: 90, maxWidth: 120, isResizable: true,
      onRender: (item: ReviewerActivityRow) => <span>{item.approved + item.rejected}</span>
    }
  ];

  const isShimmering = !activity && !error;

  return (
    <section className={classNames.dashboardSection} aria-label={labels.approvals} aria-busy={loading}>
      <DashboardCardHeader classNames={classNames} title={labels.approvals} description={labels.approvalsDescription}>
        <DashboardTimeRange
          strings={strings}
          windowDays={windowDays}
          disabled={loading}
          ariaLabel={`${labels.approvals}: ${labels.timeRange}`}
          onChange={(days) => dispatch(setApprovalWindowDays(days))} />
      </DashboardCardHeader>
      {error && <MessageBar messageBarType={MessageBarType.error}>{labels.approvalsLoadFailed}</MessageBar>}
      {activity && renderCountCards(classNames, theme, labels.total, [
        { key: 'automated', label: labels.automated, value: activity.automatedTotal, tone: 'good' },
        { key: 'manual', label: labels.manual, value: activity.manualTotal, tone: 'warning' },
        { key: 'rejected', label: labels.rejected, value: activity.rejectedTotal, tone: 'danger' }
      ])}
      {isShimmering
        ? <Spinner label={labels.loading} />
        : !hasData && !error && <Text className={classNames.dashboardEmpty}>{labels.noApprovalActivity}</Text>}
      {hasData && <DashboardActivityChart data={chartData} title={labels.approvals} />}
      {reviewers.length > 0 && (
        <details className={classNames.dashboardBreakdown}>
          <summary>{labels.reviewerBreakdown}</summary>
          <div className={classNames.dashboardTable}>
          <ShimmeredDetailsList
            items={reviewers}
            columns={reviewerColumns}
            selectionMode={SelectionMode.none}
            ariaLabelForGrid={labels.approvals}
          />
          </div>
        </details>
      )}
    </section>
  );
}

const OnBehalfOfSection: React.FunctionComponent<DashboardProps> = (props: DashboardProps) => {
  const { classNames, strings } = props;
  const dispatch = useAppDispatch();
  const theme = useTheme();
  const loadedActivity = useAppSelector(selectOnBehalfOfActivity);
  const loading = useAppSelector(selectOnBehalfOfActivityLoading);
  const error = useAppSelector(selectOnBehalfOfActivityError);
  const windowDays = useAppSelector(selectOnBehalfOfWindowDays);
  const activity = loadedActivity?.windowDays === windowDays ? loadedActivity : undefined;

  const labels = strings.Dashboard.labels;

  useEffect(() => {
    dispatch(fetchOnBehalfOfActivity(windowDays));
  }, [dispatch, windowDays]);

  const submitters = useMemo(() => activity?.submitters ?? [], [activity]);

  const chartData: IVerticalStackedChartProps[] = useMemo(() => thinAxisLabels((activity?.days ?? []).map((day) => {
    const points: IVSChartDataPoint[] = [];
    submitters.forEach((submitter, index) => {
      const hue = submitterHue(index);
      const counts = day.submitters.find((entry) => entry.submitterKey === submitter.submitterKey);
      if (counts && counts.onboarding > 0) {
        points.push({ legend: submitter.displayName, data: counts.onboarding, color: hue.onboarding });
      }
      if (counts && counts.update > 0) {
        points.push({ legend: `${submitter.displayName} (${labels.update})`, data: counts.update, color: hue.update });
      }
    });
    return { xAxisPoint: formatChartDay(day.date), chartData: points };
  }), MAX_SECTION_AXIS_LABELS), [activity, submitters, labels.update]);

  const hasData = chartData.some((day) => day.chartData.length > 0);

  const submitterColumns: IColumn[] = [
    {
      key: 'submitter', name: labels.reviewer, minWidth: 180, maxWidth: 320, isResizable: true,
      onRender: (item: SubmitterActivityRow, index?: number) => (
        <span className={mergeStyles({ display: 'flex', alignItems: 'center', gap: 8 })}>
          <span
            aria-hidden="true"
            className={mergeStyles({
              width: 10,
              height: 10,
              borderRadius: 2,
              flexShrink: 0,
              backgroundColor: submitterHue(index ?? 0).onboarding
            })} />
          <span>{item.displayName}</span>
        </span>
      )
    },
    { key: 'onboarding', name: labels.onboarding, fieldName: 'onboarding', minWidth: 90, maxWidth: 120, isResizable: true },
    { key: 'update', name: labels.update, fieldName: 'update', minWidth: 90, maxWidth: 120, isResizable: true },
    {
      key: 'total', name: labels.total, minWidth: 90, maxWidth: 120, isResizable: true,
      onRender: (item: SubmitterActivityRow) => <span>{item.onboarding + item.update}</span>
    }
  ];

  const isShimmering = !activity && !error;

  return (
    <section className={classNames.dashboardSection} aria-label={labels.requestsOnBehalfOf} aria-busy={loading}>
      <DashboardCardHeader classNames={classNames} title={labels.requestsOnBehalfOf} description={labels.onBehalfDescription}>
        <DashboardTimeRange
          strings={strings}
          windowDays={windowDays}
          disabled={loading}
          ariaLabel={`${labels.requestsOnBehalfOf}: ${labels.timeRange}`}
          onChange={(days) => dispatch(setOnBehalfOfWindowDays(days))} />
      </DashboardCardHeader>
      {error && <MessageBar messageBarType={MessageBarType.error}>{labels.onBehalfLoadFailed}</MessageBar>}
      {activity && renderCountCards(classNames, theme, labels.total, [
        { key: 'onboarding', label: labels.onboarding, value: activity.onboardingTotal, tone: 'good' },
        { key: 'update', label: labels.update, value: activity.updateTotal, tone: 'info' }
      ])}
      {isShimmering
        ? <Spinner label={labels.loading} />
        : !hasData && !error && <Text className={classNames.dashboardEmpty}>{labels.noOnBehalfActivity}</Text>}
      {hasData && <DashboardActivityChart data={chartData} title={labels.requestsOnBehalfOf} />}
      {submitters.length > 0 && (
        <details className={classNames.dashboardBreakdown}>
          <summary>{labels.reviewerBreakdown}</summary>
          <div className={classNames.dashboardTable}>
          <ShimmeredDetailsList
            items={submitters}
            columns={submitterColumns}
            selectionMode={SelectionMode.none}
            ariaLabelForGrid={labels.requestsOnBehalfOf}
          />
          </div>
        </details>
      )}
    </section>
  );
}

const Dashboard: React.FunctionComponent<DashboardProps> = (props: DashboardProps) => {
  const { classNames, strings } = props;
  const dispatch = useAppDispatch();
  const theme = useTheme();
  const summary = useAppSelector(selectDashboardSummary);
  const loading = useAppSelector(selectDashboardLoading);
  const error = useAppSelector(selectDashboardError);
  const approvalWindowDays = useAppSelector(selectApprovalWindowDays);
  const onBehalfOfWindowDays = useAppSelector(selectOnBehalfOfWindowDays);
  const approvalLoading = useAppSelector(selectApprovalActivityLoading);
  const onBehalfOfLoading = useAppSelector(selectOnBehalfOfActivityLoading);
  const anyLoading = loading || approvalLoading || onBehalfOfLoading;
  const anomalousTableRef = useRef<HTMLDivElement>(null);
  const [anomalousTableWidth, setAnomalousTableWidth] = useState(0);

  useEffect(() => {
    dispatch(fetchDashboardSummary());
  }, [dispatch]);

  const labels = strings.Dashboard.labels;

  const renderChip = useCallback((tone: DashboardTone, text: string) => {
    const statusColors = getAccessibleStatusColors(theme);
    return (
      <span className={mergeStyles({
        display: 'inline-block',
        padding: '2px 8px',
        borderRadius: 10,
        fontSize: 12,
        whiteSpace: 'nowrap',
        color: theme.palette.neutralPrimary,
        backgroundColor: tone === 'good'
          ? statusColors.successBackground
          : tone === 'warning'
            ? statusColors.warningBackground
            : tone === 'danger'
              ? statusColors.errorBackground
              : theme.palette.neutralLight,
      })}>{text}</span>
    );
  }, [theme]);

  const formatDateTime = (value?: string | null): string => {
    const parsed = parseUtc(value);
    return parsed === null
      ? labels.notAvailable
      : parsed.toLocaleString(undefined, { month: '2-digit', day: '2-digit', year: 'numeric', hour: 'numeric', minute: '2-digit' });
  };

  const formatAge = (value?: string | null): string => {
    const parsed = parseUtc(value);
    if (parsed === null) {
      return labels.notAvailable;
    }
    const days = calendarDayDiff(parsed);
    if (days >= 1) {
      return `${days}d ${labels.agoSuffix}`;
    }
    const minutes = Math.floor((Date.now() - parsed.getTime()) / 60000);
    if (minutes >= 60) {
      return `${Math.floor(minutes / 60)}h ${labels.agoSuffix}`;
    }
    return minutes >= 1 ? `${minutes}m ${labels.agoSuffix}` : labels.justNow;
  };

  const cyclesLabel = (cycles?: number | null): string => {
    if (cycles === null || cycles === undefined) {
      return labels.unknown;
    }
    if (cycles === 1) {
      return `1 ${labels.cycleSuffix}`;
    }
    return `${Math.max(cycles, 0)} ${labels.cyclesSuffix}`;
  };

  const statusTone = (status: string): DashboardTone => {
    switch (status) {
      case 'Error':
      case 'SchemaError':
      case 'ErroredDueToStuckInProgress': return 'danger';
      case 'PendingAutoApproval': return 'info';
      case 'StuckInProgress': return 'warning';
      case 'InProgress': return 'good';
      default: return 'neutral';
    }
  };

  const stalenessSummary = (row: AnomalousStatusRow): string => {
    const parts: string[] = [];
    if (row.maxCyclesMissed === null || row.maxCyclesMissed === undefined) {
      parts.push(labels.cycleCountUnknown);
    } else if (row.maxCyclesMissed >= 2) {
      parts.push(`${cyclesLabel(row.maxCyclesMissed)} ${labels.missedSuffix}`);
    }
    if (row.neverSucceededCount > 0) {
      parts.push(`${row.neverSucceededCount} ${labels.neverRanOkSuffix}`);
    }
    return parts.length > 0 ? parts.join(' · ') : labels.likelyTransient;
  };

  const renderCounts = (cards: CountCard[]) => renderCountCards(classNames, theme, labels.total, cards);

  const pendingReview = summary?.pendingReview ?? [];
  const inProgress = summary?.inProgress ?? [];
  const anomalous = summary?.anomalousStatuses ?? [];
  const isShimmering = !summary && !error;
  const pendingReviewCount = pendingReview.reduce((sum, row) => sum + row.count, 0);
  const inProgressCount = inProgress.reduce((sum, row) => sum + row.count, 0);
  const anomalousCount = anomalous.reduce((sum, row) => sum + row.count, 0);
  const showAnomalousTable = isShimmering || anomalous.length > 0;

  useEffect(() => {
    const table = anomalousTableRef.current;
    if (!showAnomalousTable || !table) {
      return;
    }
    const updateWidth = () => setAnomalousTableWidth(table.clientWidth);
    updateWidth();
    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', updateWidth);
      return () => window.removeEventListener('resize', updateWidth);
    }
    const observer = new ResizeObserver(updateWidth);
    observer.observe(table);
    return () => observer.disconnect();
  }, [showAnomalousTable]);

  const requestTypeLabel = (value: string): string =>
    value === 'Onboarding' ? labels.onboarding : value === 'Update' ? labels.update : value;

  const pendingReviewColumns: IColumn[] = [
    {
      key: 'requestType', name: labels.changeReason, minWidth: 100, maxWidth: 160, isResizable: true,
      onRender: (item: PendingReviewSummaryRow) => requestTypeLabel(item.requestType)
    },
    { key: 'count', name: labels.count, fieldName: 'count', minWidth: 45, maxWidth: 70, isResizable: true },
    {
      key: 'oldestChange', name: labels.oldestChange, minWidth: 140, maxWidth: 200, isResizable: true,
      onRender: (item: PendingReviewSummaryRow) => formatDateTime(item.oldestChangeTime)
    },
    {
      key: 'age', name: labels.age, minWidth: 65, maxWidth: 100, isResizable: true,
      onRender: (item: PendingReviewSummaryRow) => formatAge(item.oldestChangeTime)
    }
  ];

  const inProgressColumns: IColumn[] = [
    {
      key: 'status', name: labels.status, minWidth: 130, maxWidth: 240, isResizable: true,
      onRender: (item: InProgressStatusRow) => renderChip(statusTone(item.status), item.status)
    },
    { key: 'count', name: labels.count, fieldName: 'count', minWidth: 45, maxWidth: 70, isResizable: true },
    {
      key: 'oldestStarted', name: labels.oldestStarted, minWidth: 140, maxWidth: 200, isResizable: true,
      onRender: (item: InProgressStatusRow) => formatDateTime(item.oldestStartTime)
    },
    {
      key: 'age', name: labels.age, minWidth: 65, maxWidth: 100, isResizable: true,
      onRender: (item: InProgressStatusRow) => formatAge(item.oldestStartTime)
    }
  ];

  const anomalousColumns: IColumn[] = [
    {
      key: 'status', name: labels.status, fieldName: 'status', minWidth: 160, maxWidth: Math.max(160, anomalousTableWidth * 0.28), isResizable: true,
    },
    { key: 'count', name: labels.count, fieldName: 'count', minWidth: 60, maxWidth: Math.max(60, anomalousTableWidth * 0.1), isResizable: true },
    {
      key: 'cycles', name: labels.maxCyclesMissed, minWidth: 130, maxWidth: Math.max(130, anomalousTableWidth * 0.18), isResizable: true,
      onRender: (item: AnomalousStatusRow) => (
        <span title={cyclesLabel(item.maxCyclesMissed)}>
          {item.maxCyclesMissed ?? labels.notAvailable}
        </span>
      )
    },
    {
      key: 'lastSuccess', name: labels.oldestLastSuccess, minWidth: 160, maxWidth: Math.max(160, anomalousTableWidth * 0.2), isResizable: true,
      onRender: (item: AnomalousStatusRow) => (
        <span title={formatDateTime(item.oldestLastSuccessfulRun)}>
          {parseUtc(item.oldestLastSuccessfulRun)?.toLocaleDateString() ?? labels.notAvailable}
        </span>
      )
    },
    {
      key: 'neverSucceeded', name: labels.neverSucceeded, minWidth: 130, isResizable: true,
      fieldName: 'neverSucceededCount',
    }
  ];

  return (
    <section className={classNames.dashboardRoot} aria-label={labels.dashboard}>
      <div className={classNames.dashboardHeader}>
        <div>
          <Text as="h2" className={classNames.dashboardHeading}>{labels.dashboard}</Text>
          <Text className={classNames.dashboardHint}>{labels.description}</Text>
        </div>
        <div className={classNames.dashboardToolbar}>
          <DashboardTimeRange
            strings={strings}
            windowDays={approvalWindowDays === onBehalfOfWindowDays ? approvalWindowDays : 'individual'}
            disabled={anyLoading}
            ariaLabel={labels.activityTimeRange}
            onChange={(days) => {
              dispatch(setApprovalWindowDays(days));
              dispatch(setOnBehalfOfWindowDays(days));
            }} />
          <div className={classNames.dashboardRefresh} role="group" aria-label={labels.refresh}>
            {summary && (
              <Text className={classNames.dashboardSnapshot}>
                {labels.lastRefreshed.replace('{0}', formatDateTime(summary.queriedAtUtc))}
              </Text>
            )}
            <ActionButton
              iconProps={{ iconName: 'Refresh' }}
              styles={{ root: { flexShrink: 0 } }}
              disabled={anyLoading}
              onClick={() => {
                dispatch(fetchDashboardSummary());
                dispatch(fetchApprovalActivity(approvalWindowDays));
                dispatch(fetchOnBehalfOfActivity(onBehalfOfWindowDays));
              }}>
              {labels.refresh}
            </ActionButton>
          </div>
        </div>
      </div>
      {error && <MessageBar messageBarType={MessageBarType.error}>{labels.loadFailed}</MessageBar>}

      {summary && (
        <div role="group" aria-label={labels.currentSnapshot}>
          {renderCountCards(classNames, theme, undefined, [
            { key: 'anomalous', label: labels.anomalousJobs, value: anomalousCount, tone: 'danger' },
            { key: 'inProgress', label: labels.inProgress, value: inProgressCount, tone: 'warning' },
            { key: 'pendingReview', label: labels.pendingReview, value: pendingReviewCount, tone: 'info' }
          ], true)}
        </div>
      )}

      <div className={classNames.dashboardSectionRow}>
        <section className={classNames.dashboardSection} aria-label={labels.pendingReview} aria-busy={loading}>
          <DashboardCardHeader classNames={classNames} title={labels.pendingReview} description={labels.pendingReviewDescription}>
            <Text className={classNames.dashboardSnapshot}>{labels.currentSnapshot}</Text>
          </DashboardCardHeader>
          {summary && renderCounts(pendingReview.map((row) => ({
            key: row.requestType,
            label: requestTypeLabel(row.requestType),
            value: row.count,
            detail: `${labels.oldestPrefix} ${formatDateTime(row.oldestChangeTime)}`,
            tone: row.requestType === 'Onboarding' ? 'good' : row.requestType === 'Update' ? 'warning' : 'neutral'
          })))}
          {isShimmering || pendingReview.length > 0
            ? <div className={classNames.dashboardTable}>
              <ShimmeredDetailsList
                items={pendingReview}
                columns={pendingReviewColumns}
                selectionMode={SelectionMode.none}
                enableShimmer={isShimmering}
                shimmerLines={3}
                ariaLabelForGrid={labels.pendingReview}
              />
            </div>
            : !error && <Text className={classNames.dashboardEmpty}>{labels.noPendingReview}</Text>}
        </section>

        <section className={classNames.dashboardSection} aria-label={labels.inProgress} aria-busy={loading}>
          <DashboardCardHeader classNames={classNames} title={labels.inProgress} description={labels.inProgressDescription}>
            <Text className={classNames.dashboardSnapshot}>{labels.currentSnapshot}</Text>
          </DashboardCardHeader>
          {summary && renderCounts(inProgress.map((row) => ({
            key: row.status,
            label: row.status,
            value: row.count,
            detail: `${labels.oldestPrefix} ${formatDateTime(row.oldestStartTime)}`,
            tone: statusTone(row.status)
          })))}
          {isShimmering || inProgress.length > 0
            ? <div className={classNames.dashboardTable}>
              <ShimmeredDetailsList
                items={inProgress}
                columns={inProgressColumns}
                selectionMode={SelectionMode.none}
                enableShimmer={isShimmering}
                shimmerLines={3}
                ariaLabelForGrid={labels.inProgress}
              />
            </div>
            : !error && <Text className={classNames.dashboardEmpty}>{labels.noInProgress}</Text>}
        </section>
      </div>

      <section className={classNames.dashboardSection} aria-label={labels.anomalousStatuses} aria-busy={loading}>
        <DashboardCardHeader classNames={classNames} title={labels.anomalousStatuses} description={labels.anomalousDescription}>
          <Text className={classNames.dashboardSnapshot}>{labels.currentSnapshot}</Text>
        </DashboardCardHeader>
        <Text className={classNames.dashboardHint}>{labels.anomalousDescription}</Text>
        {summary && renderCounts(anomalous.map((row) => ({
          key: row.status,
          label: row.status,
          value: row.count,
          detail: stalenessSummary(row),
          tone: statusTone(row.status)
        })))}
        {showAnomalousTable
          ? <div ref={anomalousTableRef} className={classNames.dashboardTable}>
            <ShimmeredDetailsList
              items={anomalous}
              columns={anomalousColumns}
              selectionMode={SelectionMode.none}
              enableShimmer={isShimmering}
              shimmerLines={3}
              ariaLabelForGrid={labels.anomalousStatuses}
            />
          </div>
          : !error && <Text className={classNames.dashboardEmpty}>{labels.noAnomalous}</Text>}
      </section>

      <div className={classNames.dashboardSectionRow}>
        <ApprovalActivitySection classNames={classNames} strings={strings} />
        <OnBehalfOfSection classNames={classNames} strings={strings} />
      </div>
    </section>
  );
}

const GeneralSettings: React.FunctionComponent<GeneralSettingsProps> = (props: GeneralSettingsProps) => {
  const { classNames, strings, settings, setSettings, setHasValidationErrors, canEdit, serviceNotification, setServiceNotification, serviceNotificationErrors, serviceNotificationSaveError } = props;

  const handleSettingChange = (settingKey: SettingKey) => (newValue: string) => {
    setSettings((settings) => ({ ...settings, [settingKey]: newValue }));
  };

  return (
    <div>
      <SettingsSection
        classNames={classNames}
        title={strings.GeneralSettings.labels.featureControl}
        subtitle={strings.GeneralSettings.labels.featureControlDescription}
      >
        <div className={classNames.settingsGrid}>
        <GeneralSetting
          id={SettingKeyMap[SettingKey.CanReviewOwnSubmissions]}
          title={strings.GeneralSettings.labels.reviewOwnSubmissionTitle}
          description={strings.GeneralSettings.labels.reviewOwnSubmissionDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.CanReviewOwnSubmissions)}
          generalSettingValue={settings[SettingKey.CanReviewOwnSubmissions]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.CreateGroupFeatureEnabled]}
          title={strings.GeneralSettings.labels.createGroupTitle}
          description={strings.GeneralSettings.labels.createGroupDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.CreateGroupFeatureEnabled)}
          generalSettingValue={settings[SettingKey.CreateGroupFeatureEnabled]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsBusinessJustificationRequired]}
          title={strings.GeneralSettings.labels.businessJustificationTitle}
          description={strings.GeneralSettings.labels.businessJustificationDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsBusinessJustificationRequired)}
          generalSettingValue={settings[SettingKey.IsBusinessJustificationRequired]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsDisclaimerEnabled]}
          title={strings.GeneralSettings.labels.isDisclaimerEnabledTitle}
          description={strings.GeneralSettings.labels.isDisclaimerEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsDisclaimerEnabled)}
          generalSettingValue={settings[SettingKey.IsDisclaimerEnabled]}
          disabled={!canEdit}
        />
        </div>
      </SettingsSection>
      <SettingsSection
        classNames={classNames}
        title={strings.GeneralSettings.labels.userResources}
        subtitle={strings.HyperlinkSettings.labels.description}
      >
        <UserResourcesSettings
          classNames={classNames}
          strings={strings}
          settings={settings}
          setSettings={setSettings}
          canEdit={canEdit}
          setHasValidationErrors={setHasValidationErrors} />
      </SettingsSection>
      <SettingsSection
        classNames={classNames}
        title={strings.ServiceNotifications.labels.serviceNotifications}
        subtitle={strings.ServiceNotifications.labels.description}
      >
        <ServiceNotificationSettings
          classNames={classNames}
          strings={strings}
          config={serviceNotification}
          setConfig={setServiceNotification}
          errors={serviceNotificationErrors}
          saveError={serviceNotificationSaveError}
          canEdit={canEdit} />
      </SettingsSection>
    </div>
  );
}

const AutoApproverSettings: React.FunctionComponent<AutoApproverSettingsProps> = (props: AutoApproverSettingsProps) => {
  const { classNames, strings, settings, setSettings, canEdit, sqlMembershipSourceAttributes, canViewCustomSourceSettings, onNavigateToCustomSource, configured } = props;

  const handleSettingChange = (settingKey: SettingKey) => (newValue: string) => {
    setSettings((settings) => ({ ...settings, [settingKey]: newValue }));
  };

  const sensitiveNames = useMemo(
    () => (sqlMembershipSourceAttributes ?? []).filter((attribute) => attribute.isSensitive).map((attribute) => attribute.customLabel || attribute.name),
    [sqlMembershipSourceAttributes]
  );

  const labels = strings.AutoApproverSettings.labels;
  const countText = (sensitiveNames.length === 1 ? labels.sensitiveAttributeCountSingular : labels.sensitiveAttributeCountPlural)
    .replace('{0}', String(sensitiveNames.length));

  // Only shown once the attribute list has loaded, so the count is never misleading.
  const sensitiveSummary = sqlMembershipSourceAttributes === undefined ? undefined : (
    <>
      <Icon iconName="Info" className={classNames.sensitiveHintIcon} />
      <span data-testid="sensitive-attribute-summary">
        {sensitiveNames.length === 0
          ? labels.noSensitiveAttributesMarked
          : <><strong>{countText}</strong>{labels.sensitiveAttributesMarked.replace('{0}', sensitiveNames.join(', '))}</>}
      </span>
      {canViewCustomSourceSettings && (
        <>
          <span aria-hidden="true" className={classNames.sensitiveSummarySeparator}>·</span>
          <Link onClick={onNavigateToCustomSource}>{labels.manageOnCustomSource}</Link>
        </>
      )}
    </>
  );

  // Shared by the SqlMembership path cards; the group-based card has no sensitive-attribute check.
  const screeningRow = (settingKey: SettingKey) => (
    <div className={classNames.screeningRow}>
      <span>
        {labels.isSensitiveAttributeScreeningEnabledTitle}
        <TooltipHost content={labels.isSensitiveAttributeScreeningEnabledDescription}>
          <Icon iconName="Info" className={classNames.sensitiveHintIcon} aria-label={labels.isSensitiveAttributeScreeningEnabledDescription} />
        </TooltipHost>
      </span>
      <Toggle
        id={SettingKeyMap[settingKey]}
        title={labels.isSensitiveAttributeScreeningEnabledTitle}
        checked={settings[settingKey] === 'true'}
        disabled={!canEdit}
        onChange={(_, checked) => handleSettingChange(settingKey)(checked ? 'true' : 'false')}
        styles={{ root: { marginBottom: 0 } }}
      />
    </div>
  );

  return (
    <>
    <SettingsSection
      classNames={classNames}
      title={strings.AutoApproverSettings.labels.controlsTitle}
      subtitle={strings.AutoApproverSettings.labels.description}
    >
      {sensitiveSummary && <div className={classNames.sensitiveSummary}>{sensitiveSummary}</div>}
      <div className={classNames.settingsGrid}>
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]}
        title={strings.AutoApproverSettings.labels.isAutoApprovalForGroupBasedSyncsEnabledTitle}
        description={strings.AutoApproverSettings.labels.isAutoApprovalForGroupBasedSyncsEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled)}
        generalSettingValue={settings[SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]}
        disabled={!canEdit}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]}
        title={strings.AutoApproverSettings.labels.isAutoApprovalForRequestorIsOrgLeaderSyncsEnabledTitle}
        description={strings.AutoApproverSettings.labels.isAutoApprovalForRequestorIsOrgLeaderSyncsEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled)}
        generalSettingValue={settings[SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]}
        disabled={!canEdit}
        footer={screeningRow(SettingKey.IsSensitiveScreeningEnabledForOrgLeader)}
      />
      <GeneralSetting
        id={SettingKeyMap[SettingKey.IsAutoApprovalForLeaderAssistantEnabled]}
        title={strings.AutoApproverSettings.labels.isAutoApprovalForLeaderAssistantEnabledTitle}
        description={strings.AutoApproverSettings.labels.isAutoApprovalForLeaderAssistantEnabledDescription}
        onGeneralSettingChange={handleSettingChange(SettingKey.IsAutoApprovalForLeaderAssistantEnabled)}
        generalSettingValue={settings[SettingKey.IsAutoApprovalForLeaderAssistantEnabled]}
        disabled={!canEdit}
        footer={screeningRow(SettingKey.IsSensitiveScreeningEnabledForLeaderAssistant)}
      />
      <ConfiguredAttributeToggleCard
        labels={labels}
        canEdit={canEdit}
        state={configured}
        footer={screeningRow(SettingKey.IsSensitiveScreeningEnabledForConfiguredAttribute)}
      />
      </div>
    </SettingsSection>
    {(configured.enabled || configured.loadFailed) && (
      <SettingsSection
        classNames={classNames}
        title={labels.configuredRulesTitle}
        subtitle={labels.configuredRulesDescription}
      >
        <ConfiguredAttributeRules labels={labels} canEdit={canEdit} state={configured} />
      </SettingsSection>
    )}
    </>
  );
}

const UserResourcesSettings: React.FunctionComponent<UserResourcesSettingsProps> = (props: UserResourcesSettingsProps) => {

  const { classNames, strings, settings, setSettings, setHasValidationErrors, canEdit } = props;

  const [urlValidations, setUrlValidations] = useState<{ readonly [key in SettingKey]: boolean }>({
    [SettingKey.DashboardUrl]: true,
    [SettingKey.OutlookWarningUrl]: true,
    [SettingKey.PrivacyPolicyUrl]: true,
    [SettingKey.UIUrl]: true,
    [SettingKey.CanReviewOwnSubmissions]: true,
    [SettingKey.CreateGroupFeatureEnabled]: true,
    [SettingKey.IsBusinessJustificationRequired]: true,
    [SettingKey.IsDisclaimerEnabled]: true,
    [SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]: true,
    [SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]: true,
    [SettingKey.IsAutoApprovalForLeaderAssistantEnabled]: true,
    [SettingKey.IsSensitiveAttributeScreeningEnabled]: true,
    [SettingKey.IsAutoApprovalForConfiguredAttributeEnabled]: true,
    [SettingKey.ConfiguredAttributeAutoApprovalConfiguration]: true,
    [SettingKey.IsSensitiveScreeningEnabledForOrgLeader]: true,
    [SettingKey.IsSensitiveScreeningEnabledForLeaderAssistant]: true,
    [SettingKey.IsSensitiveScreeningEnabledForConfiguredAttribute]: true,
    [SettingKey.IsAITitleEnabled]: true,
    [SettingKey.IsAICopilotEnabled]: true,
    [SettingKey.IsAISearchForUserEnabled]: true,
    [SettingKey.IsAIRunExplanationEnabled]: true,
    [SettingKey.IsAIRejectionFeedbackRefinementEnabled]: true,
    [SettingKey.CopilotTemperature]: true,
    [SettingKey.CopilotTopP]: true,
    [SettingKey.CopilotInstructions]: true,
    [SettingKey.CopilotSuggestedPrompts]: true,
  });

  useEffect(() => {
    const hasValidationErrors = hasUrlValidationErrors();
    setHasValidationErrors(hasValidationErrors);
  }, [urlValidations]);

  const hasUrlValidationErrors = () => Object.values(urlValidations).some((value) => value !== true);

  const handleSettingChange = (settingKey: SettingKey) => (newValue: string) => {
    setSettings((settings) => ({ ...settings, [settingKey]: newValue }));
  };

  const handleUrlSettingValidation = (settingKey: SettingKey) => (isValid: boolean) => {
    setUrlValidations((validations) => ({ ...validations, [settingKey]: isValid }));
  };

  return (
    <div>
      <div className={classNames.tiles}>
        <HyperlinkSetting
          title={strings.HyperlinkSettings.dashboardLink.title}
          description={strings.HyperlinkSettings.dashboardLink.description}
          link={settings[SettingKey.DashboardUrl]}
          onLinkChange={handleSettingChange(SettingKey.DashboardUrl)}
          onValidation={handleUrlSettingValidation(SettingKey.DashboardUrl)}
          disabled={!canEdit}
        ></HyperlinkSetting>
        <HyperlinkSetting
          title={strings.HyperlinkSettings.outlookWarningLink.title}
          description={strings.HyperlinkSettings.outlookWarningLink.description}
          link={settings[SettingKey.OutlookWarningUrl]}
          onLinkChange={handleSettingChange(SettingKey.OutlookWarningUrl)}
          onValidation={handleUrlSettingValidation(SettingKey.OutlookWarningUrl)}
          disabled={!canEdit}
        ></HyperlinkSetting>
        <HyperlinkSetting
          title={strings.HyperlinkSettings.privacyPolicyLink.title}
          description={strings.HyperlinkSettings.privacyPolicyLink.description}
          link={settings[SettingKey.PrivacyPolicyUrl]}
          onLinkChange={handleSettingChange(SettingKey.PrivacyPolicyUrl)}
          onValidation={handleUrlSettingValidation(SettingKey.PrivacyPolicyUrl)}
          disabled={!canEdit}
        ></HyperlinkSetting>
      </div>
    </div>
  );
}

const CustomSourceSettings: React.FunctionComponent<CustomSourceSettingsProps> = (props: CustomSourceSettingsProps) => {

  const { classNames, sqlMembershipSource, sqlMembershipSourceAttributes, setNewAttributes, setNewSource, handleGetValues, setHasValidationErrors, strings, canEdit } = props;

  const [attributeMap, setAttributeMap] = useState<{ [key: string]: SqlMembershipAttribute } | undefined>(undefined);
  const [isSortedDescending, setIsSortedDescending] = useState(false);
  const [sortKey, setSortKey] = useState('attribute');
  const [sourceNameValue, setSourceNameValue] = useState(sqlMembershipSource?.customLabel || '');

  // Using useMemo to only recalculate attributes when sqlMembershipSourceAttributes change
  const attributes = useMemo(() => {
    return sqlMembershipSourceAttributes;
  }, [sqlMembershipSourceAttributes]);

  useEffect(() => {
    setSourceNameValue(sqlMembershipSource?.customLabel || '');
  }, [sqlMembershipSource?.customLabel]);

  useEffect(() => {
    const newAttributeMap = attributes?.reduce((acc: { [key: string]: SqlMembershipAttribute }, currentItem: SqlMembershipAttribute) => {
      const { name } = currentItem;
      acc[name] = {
          ...currentItem,
          ...attributeMap?.[currentItem.name]
       };
      return acc;
    }, {});

    setAttributeMap(newAttributeMap);
  }, [attributes]);

  useEffect(() => {
    const newAttributeSettings = attributeMap ? Object.values(attributeMap) : undefined;
    setNewAttributes(newAttributeSettings);
  }, [attributeMap]);

  const onSourceNameChange: (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string | undefined) => void = (event, newValue) => {
    const newSourceName = newValue ? newValue : '';
    setNewSource({ ...sqlMembershipSource, customLabel: newSourceName } as SqlMembershipSource);
    setSourceNameValue(newSourceName);
  };

  const onColumnHeaderClick: (event?: any, column?: IColumn) => void = (event, column) => {
    if (column) {
      const isSortedDescending: boolean = !!column.isSorted && !column.isSortedDescending;
      setIsSortedDescending(isSortedDescending);
      setSortKey(column.key);
    }
  };

  const handleFieldChange = useCallback(
    (attributeName: any, fieldName: any, newValue: any): any => {
      setAttributeMap((prevRowsData: any) => ({
        ...prevRowsData,
        [attributeName]: {
          ...prevRowsData[attributeName],
          [fieldName]: newValue,
        },
      }));
    },
    [setAttributeMap]
  );

  const handleDropdownClick = useCallback(
    (attribute: SqlMembershipAttribute): any => {
      if (attribute && !attribute.values) {
        handleGetValues(attribute);
      }
    },
    [attributeMap]
  );

  // The API stores the threshold as a fraction (0-1); the grid edits it as a percentage (0-100).
  const [invalidThresholds, setInvalidThresholds] = useState<{ [key: string]: boolean }>({});

  const handleNullThresholdChange = useCallback(
    (attributeName: string, raw: string, isValid: boolean): void => {
      setInvalidThresholds((previous) => ({ ...previous, [attributeName]: !isValid }));

      if (!isValid) {
        // Leave the stored value untouched while the entry is invalid; saving is blocked meanwhile.
        return;
      }

      handleFieldChange(attributeName, 'nullThreshold', raw.trim() === '' ? undefined : percentTextToFraction(raw));
    },
    [handleFieldChange]
  );

  // Block saving while any threshold entry is invalid so a bad value can't be persisted.
  useEffect(() => {
    setHasValidationErrors(Object.values(invalidThresholds).some(Boolean));
  }, [invalidThresholds, setHasValidationErrors]);

  // The Pivot unmounts this tab when another is selected. Invalid entries are never written to
  // the draft, so release the flag on the way out; otherwise the page-level Save button stays
  // permanently disabled with no visible error to explain why.
  useEffect(() => {
    return () => setHasValidationErrors(false);
  }, [setHasValidationErrors]);

  const onRenderItemColumn = (item?: any, index?: number, column?: IColumn): JSX.Element => {

    if (!item || !column || !attributeMap) {
      return <div></div>;
    }

    const fieldContent = attributeMap[item.name][column?.fieldName as keyof SqlMembershipAttribute] as string;

    switch (column?.key) {
      case 'customLabel':
        return (
          <CustomLabelCell
            value={fieldContent}
            placeholder={strings.CustomSourceSettings.labels.customLabelInputPlaceHolder}
            onChange={(e, newValue) => {
              handleFieldChange(item.name, column.fieldName, newValue);
            }}
            className={classNames.customLabelTextField}
            disabled={!canEdit}
          />
        );
      case 'attributeValues':
        return (
          <AttributeValuesCell
            classNames={classNames}
            values={attributeMap[item.name].values}
            strings={strings}
            onDropdownClick={() => {
              handleDropdownClick(attributeMap[item.name]);
            }}
          />
        );
        case 'description':
          return (
            <TextField
              value={fieldContent}
              placeholder={strings.CustomSourceSettings.labels.descriptionPlaceHolder}
              onChange={(e, newValue) => {
                handleFieldChange(item.name, column.fieldName, newValue);
              }}
              multiline rows={3}
              disabled={!canEdit}
              styles={{ fieldGroup: classNames.descriptionTextField }}
            />
          );
        case 'enabled':
          return (
            <Toggle
              title={strings.CustomSourceSettings.labels.enabledToggleTitle}
              disabled={!canEdit}
              checked={fieldContent !== undefined ? Boolean(fieldContent) : true}
              onChange={(e, checked) => handleFieldChange(item.name, column.fieldName, checked)}
            />
          );
        case 'sensitive':
          return (
            <Toggle
              title={strings.CustomSourceSettings.labels.sensitiveToggleTitle}
              disabled={!canEdit}
              checked={fieldContent !== undefined ? Boolean(fieldContent) : false}
              onChange={(e, checked) => handleFieldChange(item.name, column.fieldName, checked)}
            />
          );
        case 'nullThreshold':
          return (
            <NullThresholdCell
              key={item.name}
              attributeName={item.name}
              storedValue={attributeMap[item.name].nullThreshold}
              title={strings.CustomSourceSettings.labels.nullThresholdTitle}
              ariaLabel={`${strings.CustomSourceSettings.labels.nullThresholdColumn} ${item.name}`}
              placeholder={strings.CustomSourceSettings.labels.nullThresholdPlaceHolder}
              validationErrorMessage={strings.CustomSourceSettings.labels.nullThresholdValidationError}
              onValueChange={handleNullThresholdChange}
              disabled={!canEdit}
            />
          );
      default:
        return (
          <div className={classNames.defaultColumnSpan}>
            <Text variant="medium">{fieldContent}</Text>
          </div>
        );
    }
  };

  const columns = [
    {
      key: 'enabled',
      name: strings.CustomSourceSettings.labels.enabledColumn,
      fieldName: 'enabled',
      minWidth: 100,
      maxWidth: 120,
      isResizable: true,
      isSorted: sortKey === 'enabled',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    },
    {
      key: 'sensitive',
      name: strings.CustomSourceSettings.labels.sensitiveColumn,
      fieldName: 'isSensitive',
      minWidth: 90,
      maxWidth: 110,
      isResizable: true,
      isSorted: sortKey === 'sensitive',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    },
    {
      key: 'name',
      name: strings.CustomSourceSettings.labels.attributeColumn,
      fieldName: 'name',
      minWidth: 180,
      maxWidth: 280,
      isResizable: true,
      isSorted: sortKey === 'name',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    },
    {
      key: 'customLabel',
      name: strings.CustomSourceSettings.labels.customLabelColumn,
      fieldName: 'customLabel',
      minWidth: 160,
      maxWidth: 220,
      isResizable: true,
      isSorted: sortKey === 'customLabel',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    },
    {
      key: 'attributeValues',
      name: strings.CustomSourceSettings.labels.valuesColumn,
      fieldName: 'attributeValues',
      minWidth: 160,
      maxWidth: 200,
    },
    {
      key: 'nullThreshold',
      name: strings.CustomSourceSettings.labels.nullThresholdColumn,
      fieldName: 'nullThreshold',
      minWidth: 140,
      maxWidth: 200,
      isResizable: true,
      isSorted: sortKey === 'nullThreshold',
      isSortedDescending,
    },
    {
      key: 'description',
      name: strings.CustomSourceSettings.labels.descriptionColumn,
      fieldName: 'description',
      minWidth: 260,
      maxWidth: 520,
      isResizable: true,
      isSorted: sortKey === 'description',
      isSortedDescending,
      showSortIconWhenUnsorted: true,
    }
  ];

  const sortedItems = [...(attributes ? attributes : [])].sort((a, b) => {
    if (sortKey === 'name' || sortKey === 'customLabel') {
      return isSortedDescending
        ? (b[sortKey] || '').localeCompare(a[sortKey] || '')
        : (a[sortKey] || '').localeCompare(b[sortKey] || '');
    }
    if (sortKey === 'enabled' || sortKey === 'sensitive') {
      const aVal = (sortKey === 'sensitive' ? a.isSensitive : a.enabled) ? 1 : 0;
      const bVal = (sortKey === 'sensitive' ? b.isSensitive : b.enabled) ? 1 : 0;
      return isSortedDescending ? bVal - aVal : aVal - bVal;
    }
    if (sortKey === 'nullThreshold') {
      // Attributes without an override sort together, ahead of any explicit value.
      const aVal = a.nullThreshold ?? -1;
      const bVal = b.nullThreshold ?? -1;
      return isSortedDescending ? bVal - aVal : aVal - bVal;
    }
    return 0;
  });

  return (
    <div>
      <SettingsSection
        classNames={classNames}
        title={strings.CustomSourceSettings.labels.sourceLabeling}
        subtitle={strings.CustomSourceSettings.labels.sourceDescription}
      >
        <div className={classNames.sourceNameTextFieldContainer}>
          <TextField
            label={strings.CustomSourceSettings.labels.sourceCustomLabelInput}
            required={!sourceNameValue.trim()}
            value={sourceNameValue}
            disabled={!canEdit || sqlMembershipSource === undefined}
            onChange={onSourceNameChange}
            placeholder={strings.CustomSourceSettings.labels.customLabelInputPlaceHolder}
            styles={{ fieldGroup: classNames.sourceNameTextField }}
          />
        </div>
      </SettingsSection>

      <SettingsSection
        classNames={classNames}
        title={strings.CustomSourceSettings.labels.listOfAttributes}
        subtitle={strings.CustomSourceSettings.labels.listOfAttributesDescription}
      >
        <div className={classNames.detailsListContainer}>
          <ShimmeredDetailsList
            setKey="items"
            items={sortedItems || []}
            columns={columns}
            selectionMode={SelectionMode.none}
            onRenderItemColumn={onRenderItemColumn}
            onColumnHeaderClick={onColumnHeaderClick}
            enableShimmer={sortedItems.length === 0}
          />
        </div>
      </SettingsSection>
    </div>
  );
}

// The threshold is stored as a fraction (0-1) but edited as a percentage (0-100).
// Both conversions round away binary floating point artifacts (e.g. 0.29 * 100 = 28.999999999999996).
export const percentTextToFraction = (raw: string): number => Math.round((Number(raw) / 100) * 1e8) / 1e8;

export const fractionToPercentText = (value: number | undefined | null): string =>
  value === undefined || value === null ? '' : String(Math.round(value * 1e6) / 1e4);

export const isValidNullThresholdInput = (raw: string): boolean => {
  if (raw.trim() === '') {
    return true;
  }
  const parsed = Number(raw);
  return Number.isFinite(parsed) && parsed >= 0 && parsed <= 100;
};

// Owns its input text locally: DetailsList does not re-render a row when only the parent's
// draft state changes, so keeping the raw text here guarantees typing and validation errors
// are always reflected in the cell.
const NullThresholdCell = React.memo((props: NullThresholdCellProps) => {
  const { attributeName, storedValue, title, ariaLabel, placeholder, validationErrorMessage, disabled, onValueChange } = props;

  const [rawValue, setRawValue] = useState(fractionToPercentText(storedValue));
  const [errorMessage, setErrorMessage] = useState('');

  const handleChange = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string) => {
    const raw = newValue ?? '';
    const isValid = isValidNullThresholdInput(raw);

    setRawValue(raw);
    setErrorMessage(isValid ? '' : validationErrorMessage);
    onValueChange(attributeName, raw, isValid);
  };

  return (
    <TextField
      title={title}
      ariaLabel={ariaLabel}
      value={rawValue}
      placeholder={placeholder}
      errorMessage={errorMessage}
      disabled={disabled}
      onChange={handleChange}
    />
  );
});

const CustomLabelCell = React.memo((props: CustomLabelCellProps) => {
  const { className, value, onChange, placeholder, disabled } = props;
  return (
    <TextField
      value={value}
      styles={{ fieldGroup: className }}
      placeholder={placeholder}
      disabled={disabled}
      onChange={onChange}
    />
  );
});

const AttributeValuesCell = React.memo((props: AttributeValuesCellProps) => {
  const { classNames, values, onDropdownClick, strings } = props;

  const getDropdownOptions = (values : string[]) => {
    if (!values) {
      return [];
    }

    return values.map((value) => {
      return {
        key: value,
        text: value,
        disabled: true,
        title: value
      }
    });
  }

  const isLoading = values === undefined || values === null;

  const onRenderList: IRenderFunction<ISelectableDroppableTextProps<IDropdown, HTMLDivElement>> = (props, defaultRender) => {

    if (isLoading) {
      return (
        <div>
          <Spinner styles={{ root: classNames.valuesDropdownSpinner }} label={strings.CustomSourceSettings.labels.valuesDropdownSpinnerLabel} />
        </div>
      );
    }

    if (props?.options?.length === 0) {
      return (
        <div style={{ padding: '8px 12px' }}>
          {strings.CustomSourceSettings.labels.valuesDropdownNoValuesLabel}
        </div>
      );
    }

    return (
      <div>
        {defaultRender!(props)}
      </div>
    );
};

  return (
    <Dropdown
      title={strings.CustomSourceSettings.labels.valuesDropdownTitle}
      placeholder={strings.CustomSourceSettings.labels.valuesDropdownPlaceholder}
      onRenderList={onRenderList}
      onClick={onDropdownClick}
      options={getDropdownOptions(values)}
      dropdownWidth={'auto'}
      styles={{ dropdown: classNames.valuesDropdown, title: classNames.valuesDropdownTitle }}
    />
  );
});

const AISettings: React.FunctionComponent<AISettingsProps> = (props: AISettingsProps) => {
  const { classNames, strings, settings, setSettings, defaultAIPrompt, canEdit } = props;
  const [showDefaults, setShowDefaults] = useState(false);

  const handleSettingChange = (settingKey: SettingKey) => (newValue: string) => {
    setSettings((settings) => ({ ...settings, [settingKey]: newValue }));
  };

  return (
    <div>
      <SettingsSection
        classNames={classNames}
        title={strings.AISettings.labels.copilotAvailabilityTitle}
        subtitle={strings.AISettings.labels.copilotAvailabilityDescription}
      >
        <div className={classNames.settingsGrid}>
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsAITitleEnabled]}
          title={strings.AISettings.labels.isAITitleEnabledTitle}
          description={strings.AISettings.labels.isAITitleEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsAITitleEnabled)}
          generalSettingValue={settings[SettingKey.IsAITitleEnabled]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsAICopilotEnabled]}
          title={strings.AISettings.labels.isAICopilotEnabledTitle}
          description={strings.AISettings.labels.isAICopilotEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsAICopilotEnabled)}
          generalSettingValue={settings[SettingKey.IsAICopilotEnabled]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsAISearchForUserEnabled]}
          title={strings.AISettings.labels.isAISearchForUserEnabledTitle}
          description={strings.AISettings.labels.isAISearchForUserEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsAISearchForUserEnabled)}
          generalSettingValue={settings[SettingKey.IsAISearchForUserEnabled]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsAIRunExplanationEnabled]}
          title={strings.AISettings.labels.isAIRunExplanationEnabledTitle}
          description={strings.AISettings.labels.isAIRunExplanationEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsAIRunExplanationEnabled)}
          generalSettingValue={settings[SettingKey.IsAIRunExplanationEnabled]}
          disabled={!canEdit}
        />
        <GeneralSetting
          id={SettingKeyMap[SettingKey.IsAIRejectionFeedbackRefinementEnabled]}
          title={strings.AISettings.labels.isAIRejectionFeedbackRefinementEnabledTitle}
          description={strings.AISettings.labels.isAIRejectionFeedbackRefinementEnabledDescription}
          onGeneralSettingChange={handleSettingChange(SettingKey.IsAIRejectionFeedbackRefinementEnabled)}
          generalSettingValue={settings[SettingKey.IsAIRejectionFeedbackRefinementEnabled]}
          disabled={!canEdit}
        />
        </div>
      </SettingsSection>
      <SettingsSection
        classNames={classNames}
        showDivider
        title={strings.AISettings.labels.suggestedPromptsTitle}
        subtitle={strings.AISettings.labels.suggestedPromptsDescription}
      >
        <SuggestedPromptsEditor classNames={classNames} strings={strings} settings={settings} setSettings={setSettings} canEdit={canEdit} />
      </SettingsSection>
      <SettingsSection
        classNames={classNames}
        showDivider
        title={strings.AISettings.labels.copilotInstructionsPromptTitle}
        subtitle={strings.AISettings.labels.copilotInstructionsPromptDescription}
      >
        <Text variant="small" block className={classNames.aiSettingsLeaveEmptyNote}>
          <Icon iconName="Info" className={classNames.aiSettingsLeaveEmptyNoteIcon} />
          {strings.AISettings.labels.leaveEmptyNote}
        </Text>
        {defaultAIPrompt && (
          <div className={classNames.aiSettingsDefaultInstructionsContainer}>
            <button
              onClick={() => setShowDefaults(!showDefaults)}
              className={classNames.aiSettingsDefaultInstructionsToggle}
            >
              <Icon iconName={showDefaults ? 'ChevronDown' : 'ChevronRight'} className={classNames.aiSettingsDefaultInstructionsToggleIcon} />
              {strings.AISettings.labels.currentDefaultInstructions}
            </button>
            {showDefaults && (
              <div className={classNames.aiSettingsDefaultInstructionsContent}>
                {defaultAIPrompt}
              </div>
            )}
          </div>
        )}
        <TextField
          multiline
          rows={12}
          value={settings[SettingKey.CopilotInstructions]}
          placeholder={strings.AISettings.labels.copilotInstructionsPromptPlaceholder}
          disabled={!canEdit}
          onChange={(_, newValue) => handleSettingChange(SettingKey.CopilotInstructions)(newValue ?? '')}
        />
      </SettingsSection>
      <SettingsSection
        classNames={classNames}
        showDivider
        title={strings.AISettings.labels.modelBehaviorTitle}
        subtitle={strings.AISettings.labels.modelBehaviorDescription}
      >
        <div className={classNames.settingsGrid}>
          <ModelBehaviorSetting
            classNames={classNames}
            title={strings.AISettings.labels.copilotTemperatureTitle}
            description={strings.AISettings.labels.copilotTemperatureDescription}
            value={settings[SettingKey.CopilotTemperature]}
            fallbackValue={0.7}
            canEdit={canEdit}
            onValueChange={handleSettingChange(SettingKey.CopilotTemperature)}
          />
          <ModelBehaviorSetting
            classNames={classNames}
            title={strings.AISettings.labels.copilotTopPTitle}
            description={strings.AISettings.labels.copilotTopPDescription}
            value={settings[SettingKey.CopilotTopP]}
            fallbackValue={0.9}
            canEdit={canEdit}
            onValueChange={handleSettingChange(SettingKey.CopilotTopP)}
          />
        </div>
      </SettingsSection>
    </div>
  );
}

const ModelBehaviorSetting: React.FunctionComponent<{
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  title: string;
  description: string;
  value: string;
  fallbackValue: number;
  canEdit: boolean;
  onValueChange: (newValue: string) => void;
}> = ({ classNames, title, description, value, fallbackValue, canEdit, onValueChange }) => {
  const theme = useTheme();
  const parsed = parseFloat(value);
  return (
    <div className={classNames.modelBehaviorCard}>
      <div className={classNames.modelBehaviorHeader}>
        <Text variant="mediumPlus" className={classNames.modelBehaviorTitle}>{title}</Text>
        <Slider
          className={classNames.modelBehaviorSlider}
          min={0}
          max={1}
          step={0.05}
          value={Number.isFinite(parsed) ? parsed : fallbackValue}
          showValue
          disabled={!canEdit}
          ariaLabel={title}
          onChange={(newValue) => onValueChange(newValue.toString())}
          styles={{
            root: { flexGrow: 1, minWidth: 0 },
            container: { alignItems: 'center' },
            slideBox: { flexGrow: 1, minWidth: 0 },
            activeSection: { backgroundColor: theme.palette.themePrimary, height: 4, borderRadius: 2 },
            inactiveSection: { backgroundColor: theme.palette.themeLighter, height: 4, borderRadius: 2 },
            thumb: {
              borderColor: theme.palette.themePrimary,
              borderWidth: 2,
              width: 16,
              height: 16,
              top: -6,
            },
            valueLabel: {
              marginLeft: 12,
              minWidth: 28,
              color: theme.palette.neutralPrimary,
            },
          }}
        />
      </div>
      <Text variant="small" block className={classNames.modelBehaviorDescription}>{description}</Text>
    </div>
  );
}

const SuggestedPromptsEditor: React.FunctionComponent<{
  classNames: IProcessedStyleSet<AdminConfigStyles>;
  strings: AdminConfigViewProps['strings'];
  settings: { readonly [key in SettingKey]: string };
  setSettings: React.Dispatch<React.SetStateAction<{ readonly [key in SettingKey]: string }>>;
  canEdit: boolean;
}> = ({ classNames, strings, settings, setSettings, canEdit }) => {
  const theme = useTheme();

  const defaultSuggestedPrompts = useMemo(() => [
    { label: strings.AISettings.labels.suggestedPromptDefault1Label, prompt: strings.AISettings.labels.suggestedPromptDefault1Prompt },
    { label: strings.AISettings.labels.suggestedPromptDefault2Label, prompt: strings.AISettings.labels.suggestedPromptDefault2Prompt },
  ], [strings]);

  type PromptWithId = { id: number; label: string; prompt: string };
  const nextId = useRef(0);

  const assignIds = (items: Array<{ label: string; prompt: string }>): PromptWithId[] =>
    items.map((item) => ({ ...item, id: nextId.current++ }));

  const parsePrompts = (json: string): Array<{ label: string; prompt: string }> => {
    if (!json) return [];
    try {
      const parsed = JSON.parse(json);
      if (Array.isArray(parsed)) {
        return parsed
          .filter((item: any) => typeof item === 'object' && item !== null)
          .map((item: any) => ({
            label: typeof item.label === 'string' ? item.label : '',
            prompt: typeof item.prompt === 'string' ? item.prompt : '',
          }));
      }
    } catch { /* invalid JSON */ }
    return [];
  };

  const [prompts, setPrompts] = useState<PromptWithId[]>(() =>
    assignIds(parsePrompts(settings[SettingKey.CopilotSuggestedPrompts]))
  );

  const syncToSettings = (updated: PromptWithId[]) => {
    setPrompts(updated);
    setSettings((prev) => ({
      ...prev,
      [SettingKey.CopilotSuggestedPrompts]: JSON.stringify(
        updated.map(({ label, prompt }) => ({ label, prompt }))
      ),
    }));
  };

  const handleFieldChange = (id: number, field: 'label' | 'prompt', value: string) => {
    const updated = prompts.map((p) => (p.id === id ? { ...p, [field]: value } : p));
    syncToSettings(updated);
  };

  const handleRemove = (id: number) => {
    const updated = prompts.filter((p) => p.id !== id);
    syncToSettings(updated);
  };

  const handleAdd = () => {
    syncToSettings([...prompts, { id: nextId.current++, label: '', prompt: '' }]);
  };

  const handlePopulateDefaults = () => {
    syncToSettings(assignIds(defaultSuggestedPrompts));
  };

  return (
    <div>
      <div className={classNames.suggestedPromptsGrid}>
      {prompts.map((p) => (
        <div key={p.id} className={classNames.suggestedPromptRow}>
          <TextField
            styles={{ root: classNames.suggestedPromptLabelField }}
            placeholder={strings.AISettings.labels.suggestedPromptLabelPlaceholder}
            value={p.label}
            disabled={!canEdit}
            onChange={(_, val) => handleFieldChange(p.id, 'label', val ?? '')}
          />
          <TextField
            styles={{ root: classNames.suggestedPromptPromptField }}
            placeholder={strings.AISettings.labels.suggestedPromptPromptPlaceholder}
            value={p.prompt}
            disabled={!canEdit}
            onChange={(_, val) => handleFieldChange(p.id, 'prompt', val ?? '')}
          />
          <IconButton
            iconProps={{ iconName: 'Delete' }}
            title={strings.AISettings.labels.suggestedPromptRemove}
            ariaLabel={strings.AISettings.labels.suggestedPromptRemove}
            disabled={!canEdit}
            onClick={() => handleRemove(p.id)}
            className={classNames.suggestedPromptRemoveButton}
          />
        </div>
      ))}
      </div>
      <div className={classNames.suggestedPromptsActions}>
        <DefaultButton
          iconProps={{ iconName: 'Add' }}
          text={strings.AISettings.labels.suggestedPromptAdd}
          disabled={!canEdit}
          onClick={handleAdd}
          className={classNames.suggestedPromptAddButton}
          styles={{
            label: { color: theme.palette.neutralPrimary },
            icon: { color: theme.palette.themePrimary },
            iconHovered: { color: theme.palette.themePrimary },
            iconPressed: { color: theme.palette.themePrimary },
          }}
        />
        <ActionButton
          iconProps={{ iconName: 'Refresh' }}
          text={strings.AISettings.labels.suggestedPromptPopulateDefaults}
          disabled={!canEdit}
          onClick={handlePopulateDefaults}
          styles={{
            root: { color: theme.palette.neutralPrimary },
            label: { color: theme.palette.neutralPrimary },
            rootHovered: { color: theme.palette.neutralPrimary },
            labelHovered: { color: theme.palette.neutralPrimary },
          }}
        />
      </div>
    </div>
  );
};
