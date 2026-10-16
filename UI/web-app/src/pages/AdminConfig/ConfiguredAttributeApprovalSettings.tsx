// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import {
  ActionButton,
  DefaultButton,
  Dropdown,
  getTheme,
  IDropdownOption,
  MessageBar,
  MessageBarType,
  PrimaryButton,
  Spinner,
  TagPicker,
  ITag,
} from '@fluentui/react';
import { GeneralSetting } from '../../components/GeneralSetting';
import { AppDispatch } from '../../store';
import {
  fetchConfiguredAttributeApproval,
  saveConfiguredAttributeApproval,
} from '../../store/configuredAttributeApproval.api';
import {
  fetchAttributeMappings,
  fetchDefaultSqlMembershipSourceAttributes,
  resolveAttributeMappings,
} from '../../store/sqlMembershipSources.api';
import { ConfiguredAttributeApprovalConfiguration } from '../../apis/configuredAttributeApproval';
import { SqlMembershipAttribute } from '../../models';
import { IStrings } from '../../services/localization';

type Labels = IStrings['AdminConfig']['AutoApproverSettings']['labels'];

type Row = { attribute: string; tags: ITag[] };

const codeColumn = (attribute: SqlMembershipAttribute) => `${attribute.name}_Code`;

type Props = {
  labels: Labels;
  canEdit: boolean;
};

const friendlyName = (attribute: SqlMembershipAttribute) =>
  attribute.customLabel || attribute.name.replace(/([a-z])([A-Z])/g, '$1 $2');

export const useConfiguredAttributeApproval = (labels: Labels, canEdit: boolean, active = true) => {
  const dispatch: AppDispatch = useDispatch();
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [configuration, setConfiguration] = useState<ConfiguredAttributeApprovalConfiguration>();
  const [attributes, setAttributes] = useState<SqlMembershipAttribute[]>([]);
  const [enabled, setEnabled] = useState(false);
  const [rows, setRows] = useState<Row[]>([]);
  const [savedSnapshot, setSavedSnapshot] = useState('');
  const [message, setMessage] = useState<{ type: MessageBarType; text: string }>();
  const [searchFailed, setSearchFailed] = useState(false);

  const snapshotOf = (config: ConfiguredAttributeApprovalConfiguration) =>
    JSON.stringify({ enabled: config.enabled, attributes: (config.attributes ?? []).map((a) => ({ attribute: a.attribute, values: a.values })) });

  const load = useCallback(async () => {
    setLoading(true);
    setLoadFailed(false);
    setMessage(undefined);
    try {
      const [config, attributeList] = await Promise.all([
        dispatch(fetchConfiguredAttributeApproval()).unwrap(),
        dispatch(fetchDefaultSqlMembershipSourceAttributes()).unwrap(),
      ]);
      const eligible = attributeList.filter((a) => a.enabled && a.hasMapping);
      const loadedRows: Row[] = await Promise.all(
        (config.attributes ?? []).map(async (row) => {
          const attribute = eligible.find((a) => a.name === row.attribute);
          const resolved = attribute && row.values.length
            ? (await dispatch(resolveAttributeMappings({
                attribute: codeColumn(attribute), type: attribute.type, hasMapping: true, codes: row.values,
              })).unwrap()).mappings
            : [];
          const labelsByCode = new Map(resolved.map((m) => [m.code, m.description]));
          return { attribute: row.attribute, tags: row.values.map((v) => ({ key: v, name: labelsByCode.get(v) ?? v })) };
        })
      );
      if (canEdit && loadedRows.length < eligible.length) loadedRows.push({ attribute: '', tags: [] });
      setConfiguration(config);
      setSavedSnapshot(snapshotOf(config));
      setAttributes(eligible);
      setEnabled(config.enabled);
      setRows(loadedRows);
    } catch {
      setLoadFailed(true);
    } finally {
      setLoading(false);
    }
  }, [dispatch, canEdit]);

  useEffect(() => {
    if (active) void load();
  }, [load, active]);

  // Always offer a first field to fill in once the rule is switched on.
  useEffect(() => {
    if (enabled && canEdit && !loading && attributes.length > 0 && rows.length === 0) {
      setRows([{ attribute: '', tags: [] }]);
    }
  }, [enabled, canEdit, loading, attributes, rows]);

  const attributeOptions: IDropdownOption[] = attributes.map((a) => ({ key: a.name, text: friendlyName(a) }));

  const searchValues = async (attribute: string, filter: string): Promise<ITag[]> => {
    const selected = attributes.find((a) => a.name === attribute);
    if (!selected) return [];
    const page = await dispatch(fetchAttributeMappings({
      attribute: codeColumn(selected), type: selected.type, hasMapping: true, search: filter,
    })).unwrap();
    setSearchFailed(Boolean(page.failed));
    return page.mappings.map((m) => ({ key: m.code, name: m.description }));
  };

  const updateRow = (index: number, change: Partial<Row>) =>
    setRows((current) => current.map((row, i) => (i === index ? { ...row, ...change } : row)));

  const completeRows = rows.filter((row) => row.attribute && row.tags.length > 0);
  const pendingAttributes = completeRows.map((row) => ({ attribute: row.attribute, values: row.tags.map((t) => String(t.key)) }));
  const isDirty = !loading && !loadFailed && JSON.stringify({ enabled, attributes: pendingAttributes }) !== savedSnapshot;
  // The server rejects an enabled rule that has no attribute.
  // A half-filled row would be dropped on save, leaving a rule different from what is shown.
  const hasIncompleteRows = rows.some((row) => Boolean(row.attribute) !== (row.tags.length > 0));
  const hasErrors = hasIncompleteRows || (enabled && completeRows.length === 0);

  const save = async () => {
    if (!configuration) return;
    setMessage(undefined);
    try {
      const saved = await dispatch(
        saveConfiguredAttributeApproval({ ...configuration, enabled, attributes: pendingAttributes })
      ).unwrap();
      setConfiguration(saved);
      setSavedSnapshot(snapshotOf(saved));
    } catch {
      setMessage({ type: MessageBarType.error, text: labels.configuredSaveError });
    }
  };

  return {
    searchFailed, loading, loadFailed, load, enabled, setEnabled, rows, setRows, attributes, attributeOptions,
    message, searchValues, updateRow, isDirty, hasErrors, hasIncompleteRows, save,
  };
};

type State = ReturnType<typeof useConfiguredAttributeApproval>;

export const ConfiguredAttributeToggleCard: React.FunctionComponent<Props & { state: State }> = ({ labels, canEdit, state }) => (
  <GeneralSetting
    id="configured-attribute-enabled"
    title={labels.configuredTitle}
    description={labels.configuredDescription}
    generalSettingValue={String(state.enabled)}
    disabled={!canEdit || state.loading}
    onGeneralSettingChange={(value) => state.setEnabled(value === 'true')}
  />
);

export const ConfiguredAttributeRules: React.FunctionComponent<Props & { state: State }> = ({ labels, canEdit, state }) => {
  const { searchFailed, loading, loadFailed, load, rows, setRows, attributes, attributeOptions, message, searchValues, updateRow, hasIncompleteRows } = state;
  const theme = getTheme();
  const gridColumns = '260px minmax(0, 1fr) 32px';

  if (loading) return <Spinner label={labels.configuredLoading} data-testid="configured-attribute-loading" />;

  if (loadFailed) {
    return (
      <MessageBar
        messageBarType={MessageBarType.error}
        actions={<DefaultButton onClick={() => void load()}>{labels.configuredRetry}</DefaultButton>}
      >
        {labels.configuredLoadError}
      </MessageBar>
    );
  }

  const usedAttributes = new Set(rows.map((r) => r.attribute));

  return (
    <div
      data-testid="configured-attribute-approval"
      style={{ border: `1px solid ${theme.palette.neutralQuaternaryAlt}`, borderRadius: 8, padding: '20px 24px' }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <span style={{ fontWeight: 600 }}>{labels.configuredAttributesHeading}</span>
        {canEdit && (
          <PrimaryButton
            iconProps={{ iconName: 'Add' }}
            disabled={usedAttributes.size >= attributes.length}
            onClick={() => setRows((current) => [...current, { attribute: '', tags: [] }])}
          >
            {labels.configuredAddAttribute}
          </PrimaryButton>
        )}
      </div>
      {message && <MessageBar messageBarType={message.type}>{message.text}</MessageBar>}
      {searchFailed && (
        <MessageBar
          messageBarType={MessageBarType.error}
          data-testid="configured-attribute-search-error"
        >
          {labels.configuredSearchError}
        </MessageBar>
      )}
      {hasIncompleteRows && (
        <MessageBar messageBarType={MessageBarType.warning} data-testid="configured-attribute-incomplete">
          {labels.configuredIncompleteRow}
        </MessageBar>
      )}
      <div style={{ display: 'grid', gridTemplateColumns: gridColumns, gap: 12, fontWeight: 600, fontSize: 12, marginBottom: 4 }}>
        <span>{labels.configuredAttribute}</span>
        <span>{labels.configuredValues}</span>
      </div>
      {rows.map((row, index) => (
        <div key={index} data-testid="configured-attribute-row" style={{ display: 'grid', gridTemplateColumns: gridColumns, gap: 12, alignItems: 'start', marginBottom: 8 }}>
          <Dropdown
            placeholder={labels.configuredAttributePlaceholder}
            options={attributeOptions.filter((o) => o.key === row.attribute || !usedAttributes.has(String(o.key)))}
            selectedKey={row.attribute || undefined}
            disabled={!canEdit}
            onChange={(_, option) => updateRow(index, { attribute: String(option?.key ?? ''), tags: [] })}
          />
          <TagPicker
            key={row.attribute}
            disabled={!canEdit || !row.attribute}
            selectedItems={row.tags}
            onChange={(items) => updateRow(index, { tags: items ?? [] })}
            onResolveSuggestions={(filter, selected) =>
              searchValues(row.attribute, filter).then((tags) => tags.filter((t) => !selected?.some((s) => s.key === t.key)))
            }
            onEmptyResolveSuggestions={(selected) =>
              searchValues(row.attribute, '').then((tags) => tags.filter((t) => !selected?.some((s) => s.key === t.key)))
            }
            getTextFromItem={(item) => item.name}
            pickerSuggestionsProps={{ noResultsFoundText: searchFailed ? labels.configuredSearchError : labels.configuredNoResults }}
            inputProps={{ placeholder: labels.configuredValuesPlaceholder }}
          />
          {canEdit && (
            <ActionButton
              iconProps={{ iconName: 'Delete' }}
              ariaLabel={labels.configuredRemoveAttribute}
              onClick={() => setRows((current) => current.filter((_, i) => i !== index))}
            />
          )}
        </div>
      ))}
    </div>
  );
};
