// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useState, useRef } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { ActionButton, classNamesFunction, DefaultButton, IProcessedStyleSet, Toggle, Spinner, SpinnerSize } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { v4 as uuidv4 } from 'uuid';
import { MembershipConfigurationStyleProps, MembershipConfigurationStyles, MembershipConfigurationProps } from './MembershipConfiguration.types';
import { AdvancedQuery } from '../AdvancedQuery';
import { CopilotPanel } from '../CopilotPanel';
import { AppDispatch } from '../../store';
import {
  addSourcePart,
  buildCompositeQuery,
  clearSourceParts,
  deleteSourcePart,
  getSourcePartsFromState,
  manageMembershipAdvancedViewQuery,
  manageMembershipIsAdvancedView,
  manageMembershipIsToggleEnabled,
  setAdvancedViewQueryRaw,
  applyAdvancedViewQuery,
  setCompositeQuery,
  setIsAdvancedView,
  setIsAdvancedQueryValid,
  setSourceParts,
  updateSourcePart,
  manageMembershipIsEditingExistingJob,
} from '../../store/manageMembership.slice';
import { selectAttributes, selectAreAttributeMappingsLoading } from '../../store/sqlMembershipSources.slice';
import { SourcePart } from '../SourcePart';
import { RulesReview } from '../RulesReview';
import { useStrings, useQueryValidation } from '../../store/hooks';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';
import { selectIsJobTenantWriter, selectIsJobWriter } from '../../store/roles.slice';
import { selectIsSubmissionReviewer } from '../../store/roles.slice';
import { UserSpotCheck } from '../UserSpotCheck';
import { selectGeneratedTitlesYet, selectSelectedJobDetails, selectSelectedJobWithNoTitles, setGeneratedTitlesYet, setTitles} from '../../store/jobs.slice';
import { SyncJobQuery } from '../../models/SyncJobQuery';
import { selectOrgLeaderDataReturned } from '../../store/orgLeaderDetails.slice';
import { closePanel, openPanel, selectIsPanelOpen } from '../../store/copilot.slice';
import { fetchOrgLeaderDetails } from '../../store/orgLeaderDetails.api';
import { fetchGroupDetailsAndGenerateTitle, fetchOrgLeaderDetailsAndGenerateHRTitle, generateTitles } from '../../store/title.api';
import { HRPart } from '../../models/HRPart';
import { selectGeneratedGroupParts, selectGeneratedHRParts, selectTitles } from '../../store/title.slice';
import { selectIsAITitleEnabled } from '../../store/settings.slice';
import { useTitleProcessing } from '../../hooks/useTitleProcessing';

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
  const jobDetails = useSelector(selectSelectedJobDetails);
  const sourceParts = useSelector(getSourcePartsFromState);

  const advancedViewQuery = useSelector(manageMembershipAdvancedViewQuery) ?? '';
  const isToggleEnabled = useSelector(manageMembershipIsToggleEnabled);
  const isJobWriter = useSelector(selectIsJobWriter);
  const isJobTenantWriter = useSelector(selectIsJobTenantWriter);
  const isSubmissionReviewer = useSelector(selectIsSubmissionReviewer);
  const orgLeaderDataReturned = useSelector(selectOrgLeaderDataReturned);
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);
  const isAITitleEnabled = useSelector(selectIsAITitleEnabled);
  const jobWithNoTitles = useSelector(selectSelectedJobWithNoTitles);
  const generatedTitlesYet = useSelector(selectGeneratedTitlesYet);
  const titles = useSelector(selectTitles);
  const generatedHRParts = useSelector(selectGeneratedHRParts);
  const generatedGroupParts = useSelector(selectGeneratedGroupParts);
  const hrAttributes = useSelector(selectAttributes);

  const getAllSourcePartsExpanded = useCallback(() => {
    return sourceParts.every(part => part.isExpanded);
  }, [sourceParts]);

  const [allSourcePartsExpanded, setAllSourcePartsExpanded] = useState(() => getAllSourcePartsExpanded());
  const isCopilotPanelOpen = useSelector(selectIsPanelOpen);
  const [activeSourcePartId, setActiveSourcePartId] = useState<string | null>(null);
  const [isCopilotApplying, setIsCopilotApplying] = useState(false);
  const [copilotUsedPartIds, setCopilotUsedPartIds] = useState<Set<string>>(new Set());
  const copilotApplyStartTimeRef = useRef<number>(0);
  const copilotNeedsOrgLeaderRef = useRef(false);
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

  const handleCopilotSourcePartsGenerated = useCallback((generatedParts: ISourcePart[]) => {
    // Start loading overlay - track start time for minimum display duration
    setIsCopilotApplying(true);
    copilotApplyStartTimeRef.current = Date.now();
    const anyUseOrgStructure = generatedParts.some(p => p.useOrgStructure);
    copilotNeedsOrgLeaderRef.current = anyUseOrgStructure;

    // Fire org leader API call immediately for the FIRST part that has an objectId
    // so it runs in parallel with React re-renders
    const firstOrgPart = generatedParts.find(p => p.useOrgStructure && p.managerToAutoSelect?.objectId);
    const targetPartId = activeSourcePartId || generatedParts[0]?.id;
    if (firstOrgPart?.managerToAutoSelect?.objectId && targetPartId) {
      dispatch(fetchOrgLeaderDetails({
        objectId: firstOrgPart.managerToAutoSelect.objectId,
        key: 0,
        text: firstOrgPart.managerToAutoSelect.displayName,
        partId: targetPartId
      }));
    }
    
    // Deduplicate: skip generated parts that match already-applied source parts
    // (LLM sometimes returns previously accepted parts again)
    const isDuplicate = (part: ISourcePart): boolean => {
      return sourceParts.some(existing => {
        if (existing.query.type !== part.query.type) return false;
        if (existing.query.exclusionary !== part.query.exclusionary) return false;
        // For group membership: same groupId
        if (part.query.type === SourcePartType.GroupMembership && existing.query.type === SourcePartType.GroupMembership) {
          return part.query.source === existing.query.source;
        }
        // For HR/org: same leader identity + same depth + same filter
        if (part.useOrgStructure && existing.useOrgStructure &&
            part.query.type === SourcePartType.HR && existing.query.type === SourcePartType.HR) {
          const partLeader = part.managerToAutoSelect?.objectId ?? part.managerToAutoSelect?.email?.toLowerCase();
          const existingLeader = existing.managerToAutoSelect?.objectId ?? existing.managerToAutoSelect?.email?.toLowerCase();
          const partFilter = part.query.source.filter ?? '';
          const existingFilter = existing.query.source.filter ?? '';

          return partLeader === existingLeader &&
                 part.depthToAutoSelect === existing.depthToAutoSelect &&
                 partFilter === existingFilter;
        }
        // For HR filter-only: same filter string
        if (part.query.type === SourcePartType.HR && existing.query.type === SourcePartType.HR) {
          const partFilter = 'source' in part.query ? JSON.stringify(part.query.source) : '';
          const existingFilter = 'source' in existing.query ? JSON.stringify(existing.query.source) : '';
          return partFilter === existingFilter;
        }
        return false;
      });
    };

    if (activeSourcePartId && generatedParts.length > 0) {
      // Update the specific source part that was active when Copilot was opened
      const updatedPart = {
        ...generatedParts[0],
        id: activeSourcePartId,
        isExpanded: true,
      };
      dispatch(updateSourcePart(updatedPart));
      
      // Add remaining parts, skipping duplicates of already-applied parts
      generatedParts.slice(1).forEach(part => {
        if (!isDuplicate(part)) {
          dispatch(addSourcePart({...part, isExpanded: true}));
        }
      });
    } else {
      // No active source part - add all generated parts as new, skipping duplicates
      generatedParts.forEach(part => {
        if (!isDuplicate(part)) {
          dispatch(addSourcePart({...part, isExpanded: true}));
        }
      });
    }
    setActiveSourcePartId(null);
    setCopilotUsedPartIds(prev => {
      const next = new Set(prev);
      if (activeSourcePartId) next.add(activeSourcePartId);
      return next;
    });
    dispatch(closePanel());
  }, [dispatch, activeSourcePartId, sourceParts]);

  // Hide copilot apply overlay when data loading completes (with minimum display time)
  // For org structure flows, also wait until the org leader picker is populated
  useEffect(() => {
    if (!isCopilotApplying) return;

    const attributesReady = !areAttributeMappingsLoading;
    const orgLeaderReady = !copilotNeedsOrgLeaderRef.current || orgLeaderDataReturned === true;

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
  }, [isCopilotApplying, areAttributeMappingsLoading, orgLeaderDataReturned]);

  useEffect(() => {
    setAllSourcePartsExpanded(getAllSourcePartsExpanded());
  }, [sourceParts, getAllSourcePartsExpanded]);

  const sourcePartQuery: HRSourcePartSource = {
    manager: {
      id: undefined,
      depth: undefined
    },
    filter: ""
  };

  const newSourcePart = () => {
    const newPart: ISourcePart = {
      id: uuidv4(),
      title: "",
      query: {
        type: SourcePartType.HR,
        source: sourcePartQuery,
        exclusionary: false
      },
      isNew: true,
      isExpanded: true
    };
    dispatch(addSourcePart(newPart));
  };

  const removeSourcePart = (partId: string) => {
    dispatch(deleteSourcePart(partId));
  };

  const handleToggleChange = () => {
    const newIsAdvancedView = !isAdvancedView;

    if (newIsAdvancedView) {
      // Switching TO advanced view - convert source parts to JSON
      if (sourceParts.length > 0) {
        const currentCompositeQuery = buildCompositeQuery(sourceParts);
        // Safe to apply immediately because we constructed valid JSON from existing source parts
        dispatch(applyAdvancedViewQuery(JSON.stringify(currentCompositeQuery, null, 2)));
      }
    } else {
      // Switching FROM advanced view back to regular view - parse the advanced query
      if (advancedViewQuery && advancedViewQuery.trim() &&
          advancedViewQuery.trim() !== '[]' && advancedViewQuery.trim() !== '{}') {
        try {
          const parsedQuery: SyncJobQuery = JSON.parse(advancedViewQuery);

          // Validate that the parsed query is an array
          if (!Array.isArray(parsedQuery)) {
            console.error('Advanced view query is not an array, cannot convert to source parts');
            return;
          }

          // Convert parsed query back to source parts, preserving existing metadata where possible
          const updatedSourceParts: ISourcePart[] = parsedQuery.map((queryPart, index) => {
            // Try to find existing source part with matching query to preserve metadata
            const existingPart = sourceParts.find(part =>
              JSON.stringify(part.query) === JSON.stringify(queryPart)
            );

            // If no exact match found, check if we can preserve by index (common case for reordering)
            const fallbackPart = sourceParts[index];

            return {
              id: existingPart?.id || fallbackPart?.id || uuidv4(),
              title: existingPart?.title || fallbackPart?.title || "",
              query: queryPart,
              isNew: existingPart?.isNew || fallbackPart?.isNew || false,
              isExpanded: existingPart?.isExpanded ?? fallbackPart?.isExpanded ?? true // Default to expanded for better UX
            };
          });

          // Clear existing source parts and add the new ones
          dispatch(clearSourceParts());
          updatedSourceParts.forEach(part => dispatch(addSourcePart(part)));
        } catch (error) {
          console.error(`Error parsing advanced view query when switching back to regular view:`, error);
          // If parsing fails, don't switch views - this prevents the crash
          return;
        }
      } else {
        // If advanced query is empty or just empty brackets, clear source parts
        dispatch(clearSourceParts());
      }
    }

    dispatch(setIsAdvancedView(newIsAdvancedView));
  };

  const handleAdvancedViewQueryChange = (_event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: string) => {
    // Always update the query state to preserve user input, even if it's invalid JSON
    // Only set raw text while typing; parsing occurs on blur/explicit validation
    dispatch(setAdvancedViewQueryRaw(newValue ?? ''));
    // Mark as invalid since content changed - let validation happen on blur/explicit validation
    dispatch(setIsAdvancedQueryValid(false));
  };

  const handleExpandCollapseAll = () => {
    const newExpandedState = !allSourcePartsExpanded;
    setAllSourcePartsExpanded(newExpandedState);
    sourceParts.forEach(part => {
      dispatch(updateSourcePart({ ...part, isExpanded: newExpandedState }));
    });
  };

  useEffect(() => {
    const compositeQuery = buildCompositeQuery(sourceParts);
    dispatch(setCompositeQuery(compositeQuery));

    // Only validate the composite query in non-advanced view and when we have valid source parts
    if (!isAdvancedView && sourceParts.length > 0) {
      // Wrap in try-catch to prevent crashes during validation
      try {
        validateQuery(compositeQuery);
      } catch (error) {
        console.warn('Error during composite query validation:', error);
        dispatch(setIsAdvancedQueryValid(false));
      }
    }
  }, [dispatch, sourceParts, isAdvancedView, validateQuery]);

  // Initialize validation state when component mounts or when switching views
  useEffect(() => {
    if (!isAdvancedView && sourceParts.length === 0) {
      // Explicitly set validation to false when there are no source parts
      dispatch(setIsAdvancedQueryValid(false));
    }
  }, [dispatch, isAdvancedView, sourceParts.length]);

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

        if (isAITitleEnabled && jobWithNoTitles && !generatedTitlesYet) {

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
      {isJobTenantWriter && (
        <div className={classNames.toggleContainer}>
          <Toggle
            id="advancedViewToggle"
            inlineLabel
            onText={strings.ManageMembership.labels.advancedView}
            offText={strings.ManageMembership.labels.advancedView}
            onChange={handleToggleChange}
            checked={isAdvancedView}
            disabled={!isToggleEnabled|| orgLeaderDataReturned === false}
          />
        </div>
      )}
      {!isAdvancedView ? (
        isEditable ? (<>
        <div className={classNames.expandCollapseButton}>
          <ActionButton
              id="expandCollapseAllButton"
              iconProps={{ iconName: allSourcePartsExpanded ? 'ChevronUp' : 'ChevronDown' }}
              onClick={handleExpandCollapseAll}
            >
              {allSourcePartsExpanded ? strings.ManageMembership.labels.collapseAll : strings.ManageMembership.labels.expandAll}
            </ActionButton>
        </div>
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
          {sourceParts.map((part) => (
            <SourcePart
              key={part.id}
              partId={part.id}
              title={part.title}
              onDelete={removeSourcePart}
              totalSourceParts={sourceParts.length}
              query={part.query}
              part={part}
              isEditable={isEditable}
            />
          ))}
        </div>
        <div className={classNames.addButtonContainer}>
          <DefaultButton
            iconProps={{ iconName: 'Add' }}
            onClick={newSourcePart}
            disabled={!isJobWriter || !isEditable}>
            {strings.ManageMembership.labels.addSourcePart}
          </DefaultButton>
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
