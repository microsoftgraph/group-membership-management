// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { HRPart } from '../models/HRPart';
import { ISourcePart } from '../models/ISourcePart';
import { SourcePartType } from '../models/SourcePartType';

/**
 * Determines whether all source parts have valid (non-empty, non-stale) titles
 * suitable for persistence.
 *
 * A SqlMembership (HR) source part with a filter is considered to have a valid
 * AI-generated title only if the filter the title was generated from matches
 * the part's current filter. If the user edits the filter after the title was
 * generated (without clicking "Generate AI title" again), the title is stale
 * and this function returns false so the caller can avoid persisting stale
 * titles.
 *
 * @param sourceParts - Current source parts in the membership configuration.
 * @param aiGeneratedTitles - Titles produced by the bulk generateTitles flow.
 * @param generatedHRParts - Parts produced by the per-part HR title flow
 *   (manager + filter "Generate AI title" button).
 * @returns true when every part has a non-empty title and every HR-with-filter
 *   part has a fresh AI title.
 */
export const allSourcePartsHaveFreshTitles = (
  sourceParts: ISourcePart[],
  aiGeneratedTitles: HRPart[],
  generatedHRParts: ISourcePart[]
): boolean => {
  const sqlMembershipWithFilterCount = sourceParts.filter(
    part => part.query.type === SourcePartType.HR && part.query.source?.filter
  ).length;

  const sqlMembershipWithFilterAndFreshAiTitleCount = sourceParts.filter(part => {
    if (part.query.type !== SourcePartType.HR || !part.query.source?.filter) {
      return false;
    }
    const currentFilter = part.query.source.filter;
    const aiTitle = aiGeneratedTitles.find(t => t.partId === part.id);
    const hrTitle = generatedHRParts.find(p => p.id === part.id);
    // Title is fresh only if the filter it was generated from matches the current filter
    const aiTitleIsFresh = aiTitle !== undefined && aiTitle.filter === currentFilter;
    const hrTitleIsFresh = hrTitle !== undefined &&
      (hrTitle.query.source as { filter?: string })?.filter === currentFilter;
    return aiTitleIsFresh || hrTitleIsFresh;
  }).length;

  const aiTitleCountsMatch = sqlMembershipWithFilterCount === sqlMembershipWithFilterAndFreshAiTitleCount;
  const allPartsHaveNonEmptyTitles = sourceParts.every(part => part.title && part.title.trim() !== '');

  return aiTitleCountsMatch && allPartsHaveNonEmptyTitles;
};
