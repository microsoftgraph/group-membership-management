// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { HRPart } from '../models/HRPart';
import { ISourcePart } from '../models/ISourcePart';
import { SourcePartType } from '../models/SourcePartType';

/**
 * True when every part has a non-empty title and every HR-with-filter part has an AI title whose
 * filter matches the current filter. Only the AI cache is checked — the HR cache's filter updates
 * before the OpenAI criteria call returns, so it would falsely report fresh while the displayed
 * title still carries the previous filter's criteria.
 *
 * @param sourceParts - Current source parts in the membership configuration.
 * @param aiGeneratedTitles - Titles produced by the bulk generateTitles flow.
 * @returns true when every part has a non-empty title and every HR-with-filter
 *   part has a fresh AI title.
 */
export const allSourcePartsHaveFreshTitles = (
  sourceParts: ISourcePart[],
  aiGeneratedTitles: HRPart[]
): boolean => {
  const sqlMembershipWithFilterCount = sourceParts.filter(
    part => part.query.type === SourcePartType.HR && part.query.source?.filter
  ).length;

  const sqlMembershipWithFilterAndFreshAiTitleCount = sourceParts.filter(part => {
    if (part.query.type !== SourcePartType.HR || !part.query.source?.filter) {
      return false;
    }
    const aiTitle = aiGeneratedTitles.find(t => t.partId === part.id);
    return aiTitle !== undefined && aiTitle.filter === part.query.source.filter;
  }).length;

  const aiTitleCountsMatch = sqlMembershipWithFilterCount === sqlMembershipWithFilterAndFreshAiTitleCount;
  const allPartsHaveNonEmptyTitles = sourceParts.every(part => part.title && part.title.trim() !== '');

  return aiTitleCountsMatch && allPartsHaveNonEmptyTitles;
};
