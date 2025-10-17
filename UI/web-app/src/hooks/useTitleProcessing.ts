// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useEffect } from 'react';
import { useDispatch } from 'react-redux';
import { AppDispatch } from '../store';
import { setTitles } from '../store/jobs.slice';
import { setSourceParts } from '../store/manageMembership.slice';
import { HRSourcePartSource } from '../models/HRSourcePart';
import { ISourcePart } from '../models/ISourcePart';
import { SourcePartType } from '../models/SourcePartType';
import { combineHRTitleWithAICriteria, generateGroupTitle } from '../utils/titleGenerator';

interface UseTitleProcessingProps {
  generatedTitlesYet: boolean;
  jobWithNoTitles: boolean;
  sourceParts: ISourcePart[];
  titles: { partId: string; title?: string }[];
  generatedHRParts: { id: string; title: string }[];
  generatedGroupParts: {id: string; title: string}[];
  withSummarizedCriteriaString: string;
}

export const useTitleProcessing = ({
  generatedTitlesYet,
  jobWithNoTitles,
  sourceParts,
  titles,
  generatedHRParts,
  generatedGroupParts,
  withSummarizedCriteriaString,
}: UseTitleProcessingProps) => {
  const dispatch = useDispatch<AppDispatch>();

  useEffect(() => {
    if (generatedTitlesYet && jobWithNoTitles && sourceParts.length > 0) {

      const groupMembershipParts = sourceParts.filter((part) => part.query.type === SourcePartType.GroupMembership);

      const partsWithManagerAndFilter = sourceParts.filter(part =>
        part.query.type === SourcePartType.HR &&
        (part.query.source as HRSourcePartSource).manager?.id !== undefined &&
        (part.query.source as HRSourcePartSource).filter !== undefined
      );

      const partsWithManagerOnly = sourceParts.filter(part =>
        part.query.type === SourcePartType.HR &&
        (part.query.source as HRSourcePartSource).manager?.id !== undefined &&
        (part.query.source as HRSourcePartSource).filter === undefined
      );

      const partsWithFilterOnly = sourceParts.filter(part =>
        part.query.type === SourcePartType.HR &&
        (part.query.source as HRSourcePartSource).manager?.id === undefined &&
        (part.query.source as HRSourcePartSource).filter !== undefined
      );

      const partsNeedingAITitles = [...partsWithManagerAndFilter, ...partsWithFilterOnly];
      const hasRequiredAITitles = partsNeedingAITitles.length === 0 ||
        (titles.length > 0 && partsNeedingAITitles.every(part => titles.some(t => t.partId === part.id)));

      const hasRequiredHRTitles = partsWithManagerOnly.length === 0 ||
        (generatedHRParts.length > 0 && partsWithManagerOnly.every(part => generatedHRParts.some(hr => hr.id === part.id)));

      const hasRequiredGroupTitles = groupMembershipParts.length === 0 ||
      (generatedGroupParts.length > 0 && groupMembershipParts.every(part => generatedGroupParts.some(g => g.id === part.id)));

      if (hasRequiredAITitles && hasRequiredHRTitles && hasRequiredGroupTitles) {
        const updatedSourceParts = sourceParts.map(part => {
          const title = titles.find(t => t.partId === part.id);
          const isHRWithManager = part.query.type === SourcePartType.HR &&
                                 (part.query.source as HRSourcePartSource).manager?.id !== undefined;
          const generatedHRPart = generatedHRParts.find(hrPart => hrPart.id === part.id);
          const generatedGroupPart = generatedGroupParts.find(groupPart => groupPart.id === part.id);

          // Handle Group Membership parts
          if (part.query.type === SourcePartType.GroupMembership) {
            return {
              ...part,
              title: generatedGroupPart?.title || generateGroupTitle(
                undefined,
                part.query.source as string
              )
            };
          }

          // Handle HR parts
          return {
            ...part,
            title: combineHRTitleWithAICriteria(
              generatedHRPart?.title || "",
              title?.title,
              isHRWithManager,
              withSummarizedCriteriaString
            )
          };
        });

        const partsWithTitles = updatedSourceParts.map(part => ({
          partId: part.id,
          name: part.title
        }));

        if (partsWithTitles.length > 0) {
          dispatch(setTitles(partsWithTitles));
        }

        if (updatedSourceParts.length > 0) {
          dispatch(setSourceParts(updatedSourceParts));
        }
      }
    }
  }, [dispatch, titles, generatedHRParts, generatedGroupParts]);
};
