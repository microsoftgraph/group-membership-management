// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useState, useRef } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { classNamesFunction, IProcessedStyleSet, Spinner, SpinnerSize, MessageBar, MessageBarType } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { v4 as uuidv4 } from 'uuid';
import { MembershipConfigurationStyleProps, MembershipConfigurationStyles, MembershipConfigurationProps } from './MembershipConfiguration.types';
import { AdvancedQuery } from '../AdvancedQuery';
import { CopilotPanel } from '../CopilotPanel';
import { AppDispatch } from '../../store';
import {
  buildCompositeQuery,
  clearSourceParts,
  getSourcePartsFromState,
  manageMembershipAdvancedViewQuery,
  manageMembershipIsAdvancedView,
  setAdvancedViewQueryRaw,
  applyAdvancedViewQuery,
  setCompositeQuery,
  setIsAdvancedQueryValid,
  setSourceParts,
  manageMembershipIsEditingExistingJob,
} from '../../store/manageMembership.slice';
import { selectAttributes, selectAreAttributeMappingsLoading } from '../../store/sqlMembershipSources.slice';
import { RulesEditor } from '../RulesEditor';
import { RulesReview } from '../RulesReview';
import { useStrings, useQueryValidation } from '../../store/hooks';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';
import { selectIsJobTenantReader, selectIsJobTenantWriter, selectIsSubmissionReviewer } from '../../store/roles.slice';
import { UserSpotCheck } from '../UserSpotCheck';
import { selectGeneratedTitlesYet, selectSelectedJobDetails, selectSelectedJobWithNoTitles, setGeneratedTitlesYet, setTitles} from '../../store/jobs.slice';
import { SyncJobQuery } from '../../models/SyncJobQuery';
import { closePanel, openPanel, selectIsPanelOpen, mergeResultingParts } from '../../store/copilot.slice';
import { fetchOrgLeaderDetails } from '../../store/orgLeaderDetails.api';
import { fetchGroupDetailsAndGenerateTitle, fetchOrgLeaderDetailsAndGenerateHRTitle, generateTitles } from '../../store/title.api';
import { HRPart } from '../../models/HRPart';
import { selectGeneratedGroupParts, selectGeneratedHRParts, selectTitles } from '../../store/title.slice';
import { selectIsAITitleEnabled } from '../../store/settings.slice';
import { useTitleProcessing } from '../../hooks/useTitleProcessing';
import { applyResolvedOrgLeaderToPart } from '../../utils/orgLeader';

const getClassNames = classNamesFunction<MembershipConfigurationStyleProps, MembershipConfigurationStyles>();

export const MembershipConfigurationBase: React.FunctionComponent<MembershipConfigurationProps> = (props: MembershipConfigurationProps) => {
  const { className, styles, isEditable } = props;
  const classNames: IProcessedStyleSet<MembershipConfigurationStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const dispatch = useDispatch<AppDispatch>();
  const strings = useStrings();
  const { validateQuery } = useQueryValidation();

  const isAdvancedView = useSelector(manageMembershipIsAdvancedView);
  const isJobTenantWriter = useSelector(selectIsJobTenantWriter);
  const isJobTenantReader = useSelector(selectIsJobTenantReader);
  // The advanced view is only reachable through the toggle, which is restricted to job
  // tenant roles. Mirror that restriction here so stale state can never expose the raw
  // JSON view (or strand a user in it) for anyone else.
  const canUseAdvancedView = isJobTenantWriter || isJobTenantReader;
  const showAdvancedView = isAdvancedView && canUseAdvancedView;
  const jobDetails = useSelector(selectSelectedJobDetails);
  const sourceParts = useSelector(getSourcePartsFromState);

  const advancedViewQuery = useSelector(manageMembershipAdvancedViewQuery) ?? '';
  const isSubmissionReviewer = useSelector(selectIsSubmissionReviewer);
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);
  const isAITitleEnabled = useSelector(selectIsAITitleEnabled);
  const jobWithNoTitles = useSelector(selectSelectedJobWithNoTitles);
  const generatedTitlesYet = useSelector(selectGeneratedTitlesYet);
  const titles = useSelector(selectTitles);
  const generatedHRParts = useSelector(selectGeneratedHRParts);
  const generatedGroupParts = useSelector(selectGeneratedGroupParts);
  const hrAttributes = useSelector(selectAttributes);

  const isCopilotPanelOpen = useSelector(selectIsPanelOpen);
  const [activeSourcePartId, setActiveSourcePartId] = useState<string | null>(null);
  const [isCopilotApplying, setIsCopilotApplying] = useState(false);
  const [copilotUsedPartIds, setCopilotUsedPartIds] = useState<Set<string>>(new Set());
  const copilotApplyStartTimeRef = useRef<number>(0);
  const copilotNeedsOrgLeaderRef = useRef(false);
  const titlesRequestedForJobIdRef = useRef<string | undefined>(undefined);
  const [copilotUnresolvedLeaders, setCopilotUnresolvedLeaders] = useState<string[]>([]);
  // True once an in-flight Copilot apply has finished resolving all org leaders and committed
  // the parts. Drives the overlay dismissal directly instead of the shared orgLeaderDataReturned
  // flag, which is unreliable across concurrent/rejected lookups.
  const [copilotOrgLeadersResolved, setCopilotOrgLeadersResolved] = useState(false);
  const areAttributeMappingsLoading = useSelector(selectAreAttributeMappingsLoading);

  // Reset panel state on mount (prevents auto-open from stale Redux state)
  useEffect(() => {
    dispatch(closePanel());
  }, [dispatch]);

  // When panel opens from the step header button, auto-select first HR source part
  useEffect(() => {
    if (isCopilotPanelOpen && !activeSourcePartId) {
      const firstHRPart = sourceParts.find(p => p.query.type === SourcePartType.HR);
      if (firstHRPart) {
        setActiveSourcePartId(firstHRPart.id);
      }
    }
  }, [isCopilotPanelOpen, activeSourcePartId, sourceParts]);

  const handleCopilotSourcePartsGenerated = useCallback(async (generatedParts: ISourcePart[]) => {
    // Start loading overlay - track start time for minimum display duration
    setIsCopilotApplying(true);
    setCopilotOrgLeadersResolved(false);
    copilotApplyStartTimeRef.current = Date.now();
    const anyUseOrgStructure = generatedParts.some(p => p.useOrgStructure);
    copilotNeedsOrgLeaderRef.current = anyUseOrgStructure;

    try {
      // Resolve every org-structure leader (objectId -> employeeId) up front and bake the
      // employeeId into the part, so the rule card AND the rule editor read one persisted source
      // of truth (query.source.manager.id) instead of the transient managerToAutoSelect hint —
      // independent of which rule's editor happens to be mounted.
      const unresolved: string[] = [];
      const resolvedParts = await Promise.all(generatedParts.map(async (part) => {
        if (part.query.type === SourcePartType.HR && part.useOrgStructure && part.managerToAutoSelect?.objectId) {
          try {
            const details = await dispatch(fetchOrgLeaderDetails({
              objectId: part.managerToAutoSelect.objectId,
              key: 0,
              text: part.managerToAutoSelect.displayName,
              partId: part.id,
            })).unwrap();
            if (details.employeeId > 0) {
              return applyResolvedOrgLeaderToPart(part, details.employeeId, details.maxDepth);
            }
            // Leader isn't present in the HR source (employeeId 0): keep the hint so the picker
            // still shows it and the user can pick manually, but tell them it wasn't applied.
            unresolved.push(part.managerToAutoSelect.displayName);
          } catch {
            unresolved.push(part.managerToAutoSelect.displayName);
          }
        }
        return part;
      }));
      setCopilotUnresolvedLeaders(unresolved);

      // The resulting query is the complete, authoritative set of parts (Copilot preserves
      // parts it cannot edit). Apply it BY id: matching ids keep their transient UI state,
      // new server-minted parts are appended, and removed parts drop out. No global isNew forcing.
      const merged = mergeResultingParts(sourceParts, resolvedParts).map(part =>
        part.isNew ? { ...part, isExpanded: true } : part
      );
      dispatch(setSourceParts(merged));
      setActiveSourcePartId(null);
      setCopilotUsedPartIds(prev => {
        const next = new Set(prev);
        if (activeSourcePartId) next.add(activeSourcePartId);
        return next;
      });
      dispatch(closePanel());
    } finally {
      // Resolution + commit are done (even if a lookup threw): let the overlay dismiss.
      setCopilotOrgLeadersResolved(true);
    }
  }, [dispatch, activeSourcePartId, sourceParts]);

  // Hide copilot apply overlay when data loading completes (with minimum display time)
  // For org structure flows, also wait until the org leader picker is populated
  useEffect(() => {
    if (!isCopilotApplying) return;

    const attributesReady = !areAttributeMappingsLoading;
    const orgLeaderReady = !copilotNeedsOrgLeaderRef.current || copilotOrgLeadersResolved;

    if (attributesReady && orgLeaderReady) {
      const elapsed = Date.now() - copilotApplyStartTimeRef.current;
      const minDisplayTime = 800; // Show overlay for at least 800ms for smooth UX
      const remainingTime = Math.max(0, minDisplayTime - elapsed);
      
      const timer = setTimeout(() => {
        setIsCopilotApplying(false);
        copilotNeedsOrgLeaderRef.current = false;
      }, remainingTime);
      
      return () => clearTimeout(timer);
    }
  }, [isCopilotApplying, areAttributeMappingsLoading, copilotOrgLeadersResolved]);

  const handleAdvancedViewQueryChange = (_event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: string) => {
    // Always update the query state to preserve user input, even if it's invalid JSON
    // Only set raw text while typing; parsing occurs on blur/explicit validation
    dispatch(setAdvancedViewQueryRaw(newValue ?? ''));
    // Mark as invalid since content changed - let validation happen on blur/explicit validation
    dispatch(setIsAdvancedQueryValid(false));
  };

  useEffect(() => {
    const compositeQuery = buildCompositeQuery(sourceParts);
    dispatch(setCompositeQuery(compositeQuery));

    // Only validate the composite query when the user can actually edit it, in
    // non-advanced view, and when we have valid source parts. Read-only views
    // (e.g. the source parts panel on JobDetails) never consume the result, and
    // validation calls a writer-only WebApi endpoint that would 403 for readers.
    if (isEditable && !showAdvancedView && sourceParts.length > 0) {
      // Wrap in try-catch to prevent crashes during validation
      try {
        validateQuery(compositeQuery);
      } catch (error) {
        console.warn('Error during composite query validation:', error);
        dispatch(setIsAdvancedQueryValid(false));
      }
    }
  }, [dispatch, sourceParts, showAdvancedView, isEditable, validateQuery]);

  // Initialize validation state when component mounts or when switching views
  useEffect(() => {
    if (!showAdvancedView && sourceParts.length === 0) {
      // Explicitly set validation to false when there are no source parts
      dispatch(setIsAdvancedQueryValid(false));
    }
  }, [dispatch, showAdvancedView, sourceParts.length]);

  useEffect(() => {
    // Always re-initialize from DB when NOT editing
    if (jobDetails?.query && !isEditingExistingJob) {
      try {
        const parsedQuery: SyncJobQuery = JSON.parse(jobDetails.query);
        const updatedSourceParts = parsedQuery.map((query, index) => {
          const originalPart = sourceParts[index];
          const partId = jobWithNoTitles && jobDetails.titles.length === 0 ? uuidv4() : (jobDetails.titles[index]?.partId ?? uuidv4());
          return {
            id: partId,
            title: jobWithNoTitles ? "" : (jobDetails.titles[index]?.name ?? ""),
            query: query,
            isValid: true,
            isNew: originalPart?.isNew ?? false,
            isExpanded: originalPart?.isExpanded ?? false
          }
        });

        // Initialize titles array with the same part IDs when jobWithNoTitles is true
        if (isAITitleEnabled && jobWithNoTitles && !generatedTitlesYet) {
          const initialTitles = updatedSourceParts.map(part => ({ partId: part.id, name: '' }));
          dispatch(setTitles(initialTitles));
        }

        const jobId = jobDetails.syncJobId;
        if (isAITitleEnabled && jobWithNoTitles && !generatedTitlesYet
          && jobId
          && titlesRequestedForJobIdRef.current !== jobId) {
          titlesRequestedForJobIdRef.current = jobId;

          const groupMembershipParts = updatedSourceParts.filter((part) => part.query.type === SourcePartType.GroupMembership);
          groupMembershipParts.forEach(part => {
            dispatch(fetchGroupDetailsAndGenerateTitle({ part, strings }));
          });

          const partsWithFilter = updatedSourceParts.filter((part) => part.query.type === SourcePartType.HR && (part.query.source as HRSourcePartSource).filter !== undefined);
          const partsWithManagerAndFilter = partsWithFilter.filter((part) => part.query.type === SourcePartType.HR && (part.query.source as HRSourcePartSource).manager?.id !== undefined);

          const partsWithNoFilter = updatedSourceParts.filter((part) => part.query.type === SourcePartType.HR && (part.query.source as HRSourcePartSource).filter === undefined);
          const partsWithManagerAndNoFilter = partsWithNoFilter.filter((part) => part.query.type === SourcePartType.HR && (part.query.source as HRSourcePartSource).manager?.id !== undefined);

          const allPartsWithManager = [...partsWithManagerAndFilter, ...partsWithManagerAndNoFilter];
          allPartsWithManager.forEach(part => {
            dispatch(fetchOrgLeaderDetailsAndGenerateHRTitle({ part, strings }));
          });

          if (partsWithFilter.length > 0) {
            const titleList: HRPart[] = partsWithFilter.map(item => ({
              partId: item.id,
              filter: (item.query.source as HRSourcePartSource).filter as string,
              title: ""
            }));
            dispatch(generateTitles(titleList));
          }

          if (allPartsWithManager.length > 0 || partsWithFilter.length > 0 || groupMembershipParts.length > 0) {
            dispatch(setGeneratedTitlesYet(true));
          }
        }

        dispatch(setSourceParts(updatedSourceParts));
        // Existing job query should already be valid JSON
        dispatch(applyAdvancedViewQuery(jobDetails.query));
        dispatch(setCompositeQuery(parsedQuery));
      } catch (error) {
        console.error(`Error parsing job details query:`, error);
      }
    }
    // If editing, do NOT overwrite local state
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dispatch, jobDetails, isEditingExistingJob]);

  // Handle title processing and combination logic
  useTitleProcessing({
    generatedTitlesYet,
    jobWithNoTitles,
    sourceParts,
    titles,
    generatedHRParts,
    generatedGroupParts,
    withSummarizedCriteriaString: strings.HROnboarding.withSummarizedCriteria,
  });

  return (
    <div>
      {!showAdvancedView ? (
        isEditable ? (<>
        <div style={{ position: 'relative' }}>
          {/* Loading overlay when Copilot is applying filters */}
          {isCopilotApplying && (
            <div style={{
              position: 'absolute',
              top: 0,
              left: 0,
              right: 0,
              bottom: 0,
              backgroundColor: 'rgba(255, 255, 255, 0.9)',
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              zIndex: 100,
              borderRadius: '4px',
              minHeight: '200px',
              gap: '12px'
            }}>
              <Spinner size={SpinnerSize.large} />
              <span style={{ fontSize: '14px', color: '#323130', fontWeight: 500 }}>
                Setting up your membership query...
              </span>
            </div>
          )}
          {copilotUnresolvedLeaders.length > 0 && (
            <MessageBar
              messageBarType={MessageBarType.warning}
              isMultiline
              onDismiss={() => setCopilotUnresolvedLeaders([])}
              dismissButtonAriaLabel="Close"
              styles={{ root: { marginBottom: 8 } }}
            >
              {`Couldn't set ${copilotUnresolvedLeaders.join(', ')} as the org leader — not found in the HR source. Please pick a leader manually for that rule.`}
            </MessageBar>
          )}
          <RulesEditor isEditable={isEditable} />
        </div>
      </>) : (<>
        {isSubmissionReviewer && jobDetails?.syncJobId && (
          <UserSpotCheck syncJobId={jobDetails.syncJobId} sourceParts={sourceParts} />
        )}
        <RulesReview parts={sourceParts} showTitle={false} />
      </>)
      ) : (<div className={classNames.card}>
        <AdvancedQuery
          query={advancedViewQuery}
          onQueryChange={handleAdvancedViewQueryChange}
          partId={1}
          isEditable={isEditable}
        />
      </div>
      )}
      <CopilotPanel
        isOpen={isCopilotPanelOpen}
        dismissPanel={() => dispatch(closePanel())}
        onSourcePartsGenerated={handleCopilotSourcePartsGenerated}
        hrAttributes={hrAttributes?.filter(attr => attr.enabled).map(attr => ({
          name: attr.name,
          hasMapping: attr.hasMapping,
          customLabel: attr.customLabel,
          description: attr.description
        }))}
        sourcePartId={activeSourcePartId || undefined}
      />
    </div>
  );
};
