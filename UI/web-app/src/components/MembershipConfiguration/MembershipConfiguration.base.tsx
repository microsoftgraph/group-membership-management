// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { ActionButton, classNamesFunction, DefaultButton, IProcessedStyleSet, Toggle } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { v4 as uuidv4 } from 'uuid';
import { MembershipConfigurationStyleProps, MembershipConfigurationStyles, MembershipConfigurationProps } from './MembershipConfiguration.types';
import { AdvancedQuery } from '../AdvancedQuery';
import { AppDispatch } from '../../store';
import {
  addSourcePart,
  buildCompositeQuery,
  clearSourceParts,
  deleteSourcePart,
  getSourcePartsFromState,
  manageMembershipAdvancedViewQuery,
  manageMembershipCompositeQuery,
  manageMembershipIsAdvancedView,
  manageMembershipIsToggleEnabled,
  manageMembershipQuery,
  setAdvancedViewQuery,
  setCompositeQuery,
  setIsAdvancedView,
  setIsAdvancedQueryValid,
  setSourceParts,
  updateSourcePart,
  manageMembershipIsEditingExistingJob,
} from '../../store/manageMembership.slice';
import { SourcePart } from '../SourcePart';
import { useStrings } from '../../store/hooks';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';
import { selectIsJobTenantWriter, selectIsJobWriter } from '../../store/roles.slice';
import { selectSelectedJobDetails } from '../../store/jobs.slice';
import { SyncJobQuery } from '../../models/SyncJobQuery';
import { selectOrgLeaderDataReturned } from '../../store/orgLeaderDetails.slice';

const getClassNames = classNamesFunction<MembershipConfigurationStyleProps, MembershipConfigurationStyles>();

export const MembershipConfigurationBase: React.FunctionComponent<MembershipConfigurationProps> = (props: MembershipConfigurationProps) => {
  const { className, styles, isEditable } = props;
  const classNames: IProcessedStyleSet<MembershipConfigurationStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const dispatch = useDispatch<AppDispatch>();
  const strings = useStrings();

  const isAdvancedView = useSelector(manageMembershipIsAdvancedView);
  const jobDetails = useSelector(selectSelectedJobDetails);
  const sourceParts = useSelector(getSourcePartsFromState);

  const globalQuery = useSelector(manageMembershipQuery);
  const advancedViewQuery = useSelector(manageMembershipAdvancedViewQuery) ?? '';
  const compositeQuery = useSelector(manageMembershipCompositeQuery) ?? globalQuery;
  const isToggleEnabled = useSelector(manageMembershipIsToggleEnabled);
  const isJobWriter = useSelector(selectIsJobWriter);
  const isJobTenantWriter = useSelector(selectIsJobTenantWriter);
  const orgLeaderDataReturned = useSelector(selectOrgLeaderDataReturned);
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);

  const getAllSourcePartsExpanded = () => {
    return sourceParts.every(part => part.isExpanded);
  }

  const [allSourcePartsExpanded, setAllSourcePartsExpanded] = useState(getAllSourcePartsExpanded);

  useEffect(() => {
    setAllSourcePartsExpanded(getAllSourcePartsExpanded());
  }, [sourceParts]);

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
      if (!(sourceParts.length === 0)) {
        const currentCompositeQuery = buildCompositeQuery(sourceParts);
        dispatch(setAdvancedViewQuery(JSON.stringify(currentCompositeQuery)));
      }
    } else {
      // When switching back to non-advanced view
      if (compositeQuery) {
        try {
          const updatedSourceParts: ISourcePart[] = compositeQuery.map((query, index) => {
            const originalPart = sourceParts[index];
            const newPart: ISourcePart = {
              id: uuidv4(),
              title: "",
              query: {
                type: SourcePartType.HR,
                source: sourcePartQuery,
                exclusionary: false
              },
              isNew: originalPart?.isNew ?? false,
              isExpanded: originalPart?.isExpanded ?? false
            };
            return newPart;
          });
          dispatch(clearSourceParts());
          updatedSourceParts.forEach(part => dispatch(addSourcePart(part)));
        } catch (error) {
          console.error(`Error parsing advanced view query:`, error);
        }
      }
    }

    dispatch(setIsAdvancedView(newIsAdvancedView));
  };

  const handleAdvancedViewQueryChange = (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: string) => {
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
  }, [dispatch, sourceParts]);
  
  useEffect(() => {
    // Always re-initialize from DB when NOT editing
    if (jobDetails?.query && !isEditingExistingJob) {
      try {
        const parsedQuery: SyncJobQuery = JSON.parse(jobDetails.query);
        const updatedSourceParts = parsedQuery.map((query, index) => {
          const originalPart = sourceParts[index];
          return {
            id: jobDetails.titles && jobDetails.titles[index] ? jobDetails.titles[index].partId : uuidv4(),
            title: jobDetails.titles && jobDetails.titles[index] ? jobDetails.titles[index].name : "",
            query: query,
            isValid: true,
            isNew: originalPart?.isNew ?? false,
            isExpanded:  isEditingExistingJob ? originalPart?.isExpanded ?? true : false
          }
        });
        dispatch(setSourceParts(updatedSourceParts));
        dispatch(setAdvancedViewQuery(jobDetails.query));
        dispatch(setCompositeQuery(parsedQuery));
      } catch (error) {
        console.error(`Error parsing job details query:`, error);
      }
    }
    // If editing, do NOT overwrite local state
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dispatch, jobDetails, isEditingExistingJob]);

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
      {!isAdvancedView ? (<>
        <div className={classNames.expandCollapseButton}>
          <ActionButton
              id="expandCollapseAllButton"
              iconProps={{ iconName: allSourcePartsExpanded ? 'ChevronUp' : 'ChevronDown' }}
              onClick={handleExpandCollapseAll}
            >
              {allSourcePartsExpanded ? strings.ManageMembership.labels.collapseAll : strings.ManageMembership.labels.expandAll}
            </ActionButton>
        </div>
        <div>
          {sourceParts.map((part, index) => (
            <SourcePart
              key={part.id}
              partId={part.id}
              title={jobDetails && jobDetails.titles && jobDetails.titles[index] ? jobDetails.titles[index].name : part.title}
              onDelete={removeSourcePart}
              totalSourceParts={sourceParts.length}
              query={part.query}
              part={part}
              isEditable={isEditable}
            />
          ))}
        </div>
        {isEditable &&
          <div className={classNames.addButtonContainer}>
            <DefaultButton
              iconProps={{ iconName: 'Add' }}
              onClick={newSourcePart}
              disabled={!isJobWriter || !isEditable}>
              {strings.ManageMembership.labels.addSourcePart}
            </DefaultButton>
          </div>
        }
      </>) : (<div className={classNames.card}>
        <AdvancedQuery
          query={advancedViewQuery}
          onQueryChange={handleAdvancedViewQueryChange}
          partId={1}
          isEditable={isEditable}
        />
      </div>
      )}
    </div>
  );
};
