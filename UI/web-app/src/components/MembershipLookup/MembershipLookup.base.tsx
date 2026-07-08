// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import {
  classNamesFunction,
  type IProcessedStyleSet,
  type IPersonaProps,
  Icon,
  Label,
  MessageBar,
  MessageBarType,
  NormalPeoplePicker,
  Persona,
  PersonaSize,
  Spinner,
  SpinnerSize,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import type {
  MembershipLookupProps,
  MembershipLookupStyleProps,
  MembershipLookupStyles,
} from './MembershipLookup.types';
import { useStrings } from '../../store/hooks';
import { AppDispatch } from '../../store';
import { getPeoplePickerSuggestions } from '../../store/jobs.api';
import { searchSyncHistoryByUser } from '../../store/jobDetails.api';
import { MembershipChangeType, type SearchSyncHistoryByUserResult } from '../../models/SearchSyncHistoryByUserResult';

export const getClassNames = classNamesFunction<MembershipLookupStyleProps, MembershipLookupStyles>();

type LookupStatus = 'idle' | 'loading' | 'succeeded' | 'failed';

export type PendingAction = 'add' | 'remove' | 'none';

type LookupResult = {
  persona: IPersonaProps;
  isMember: boolean;
  pendingAction: PendingAction;
};

/**
 * Derives the "Current status" and "After changes" facts for a user from a
 * searchSyncHistoryByUser result, scoped to the threshold-exceeded run.
 * - isMember: whether the user is currently in the destination group.
 * - pendingAction: whether this run's pending change adds/removes the user, or no change.
 */
export const evaluateLookup = (
  data: SearchSyncHistoryByUserResult,
  runId: string
): { isMember: boolean; pendingAction: PendingAction } => {
  const isMember = data.checkedCurrentGroupMembership ? data.userInCurrentGroup : false;
  const change = data.runMembershipChanges.find(
    (c) => c.runId?.toLowerCase() === runId.toLowerCase()
  );
  const pendingAction: PendingAction = change
    ? change.membershipChangeType === MembershipChangeType.Added
      ? 'add'
      : 'remove'
    : 'none';
  return { isMember, pendingAction };
};

export const MembershipLookupBase: React.FunctionComponent<MembershipLookupProps> = (props: MembershipLookupProps) => {
  const { className, styles, syncJobId, runId } = props;
  const classNames: IProcessedStyleSet<MembershipLookupStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();
  const lookupStrings = strings.Components.MembershipLookup;
  const dispatch = useDispatch<AppDispatch>();
  const theme = useTheme();

  const [status, setStatus] = useState<LookupStatus>('idle');
  const [result, setResult] = useState<LookupResult | null>(null);

  // Reset when the job/run being reviewed changes.
  useEffect(() => {
    setStatus('idle');
    setResult(null);
  }, [syncJobId, runId]);

  const onResolveSuggestions = useCallback(
    async (filterText: string): Promise<IPersonaProps[]> => {
      const trimmed = filterText?.trim();
      if (!trimmed) {
        return [];
      }
      try {
        const users = await dispatch(getPeoplePickerSuggestions(trimmed)).unwrap();
        return users.map((user) => ({
          key: user.id,
          id: user.id,
          text: user.text,
          secondaryText: user.secondaryText,
        }));
      } catch {
        return [];
      }
    },
    [dispatch]
  );

  const evaluate = useCallback(
    (persona: IPersonaProps, data: SearchSyncHistoryByUserResult): LookupResult => {
      const { isMember, pendingAction } = evaluateLookup(data, runId);
      return { persona, isMember, pendingAction };
    },
    [runId]
  );

  const handlePickerChange = useCallback(
    async (items?: IPersonaProps[]): Promise<void> => {
      if (!items || items.length === 0 || !items[0].id) {
        setStatus('idle');
        setResult(null);
        return;
      }
      const persona = items[0];
      setStatus('loading');
      setResult(null);
      try {
        const data = await dispatch(
          searchSyncHistoryByUser({ syncJobId, userObjectId: persona.id! })
        ).unwrap();
        setResult(evaluate(persona, data));
        setStatus('succeeded');
      } catch {
        setStatus('failed');
        setResult(null);
      }
    },
    [dispatch, evaluate, syncJobId]
  );

  const renderCurrentStatus = (isMember: boolean): JSX.Element => (
    <span className={classNames.statusValue}>
      <Icon
        className={classNames.statusIcon}
        iconName={isMember ? 'CompletedSolid' : 'CircleRing'}
        style={{ color: isMember ? theme.palette.themePrimary : theme.palette.neutralSecondary }}
      />
      <span>{isMember ? lookupStrings.isMember : lookupStrings.isNotMember}</span>
    </span>
  );

  const renderAfterChanges = (pendingAction: PendingAction): JSX.Element => {
    const iconName =
      pendingAction === 'add' ? 'CircleAdditionSolid' : pendingAction === 'remove' ? 'CompletedSolid' : 'CircleFill';
    const iconColor = pendingAction === 'none' ? theme.palette.neutralTertiary : theme.palette.themePrimary;
    const label =
      pendingAction === 'add'
        ? lookupStrings.willBeAdded
        : pendingAction === 'remove'
          ? lookupStrings.willBeRemoved
          : lookupStrings.noChange;
    return (
      <span className={classNames.statusValue}>
        <Icon className={classNames.statusIcon} iconName={iconName} style={{ color: iconColor }} />
        <span>{label}</span>
      </span>
    );
  };

  const hasResult = status === 'succeeded' && result != null;

  return (
    <div className={classNames.root}>
      <div className={classNames.title}>
        {hasResult ? lookupStrings.reviewImpactedMembers : lookupStrings.title}
      </div>
      <div className={classNames.description}>{lookupStrings.description}</div>
      {hasResult && <Label htmlFor="membershipLookupPicker">{lookupStrings.title}</Label>}
      <NormalPeoplePicker
        inputProps={{ id: 'membershipLookupPicker', 'aria-label': lookupStrings.searchLabel }}
        onResolveSuggestions={onResolveSuggestions}
        pickerSuggestionsProps={{
          suggestionsHeaderText: lookupStrings.suggestedText,
          noResultsFoundText: lookupStrings.noResultsFoundText,
          loadingText: lookupStrings.loadingText,
        }}
        key="membershipLookup"
        selectionAriaLabel={lookupStrings.selectionAriaLabel}
        removeButtonAriaLabel={lookupStrings.removeButtonAriaLabel}
        resolveDelay={600}
        itemLimit={1}
        onChange={handlePickerChange}
        styles={{ text: classNames.picker }}
        pickerCalloutProps={{ calloutMinWidth: 360 }}
      />

      {status === 'loading' && (
        <div className={classNames.spinnerContainer}>
          <Spinner size={SpinnerSize.small} />
          <span>{lookupStrings.checking}</span>
        </div>
      )}

      {status === 'failed' && (
        <MessageBar className={classNames.errorMessage} messageBarType={MessageBarType.error}>
          {lookupStrings.errorMessage}
        </MessageBar>
      )}

      {status === 'succeeded' && result && (
        <div className={classNames.resultCard}>
          <Persona
            className={classNames.resultPersona}
            text={result.persona.text}
            secondaryText={result.persona.secondaryText}
            size={PersonaSize.size40}
          />
          <div className={classNames.resultStatuses}>
            <span className={classNames.statusLabel}>{lookupStrings.currentStatusLabel}</span>
            {renderCurrentStatus(result.isMember)}
            <span className={classNames.statusLabel}>{lookupStrings.afterChangesLabel}</span>
            {renderAfterChanges(result.pendingAction)}
          </div>
        </div>
      )}
    </div>
  );
};
