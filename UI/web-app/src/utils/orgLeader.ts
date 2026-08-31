// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { HRSourcePart, HRSourcePartSource } from '../models/HRSourcePart';
import { ISourcePart } from '../models/ISourcePart';
import { SourcePartType } from '../models/SourcePartType';

/**
 * Clamp the org-hierarchy depth against the resolved leader's maximum depth.
 * If a previously-chosen depth exceeds the new leader's maxDepth it is dropped
 * (so the depth dropdown isn't left showing an invalid level); otherwise the
 * existing depth wins, falling back to the Copilot-provided auto-select depth.
 */
export function clampOrgLeaderDepth(
  existingDepth: number | undefined,
  autoSelectDepth: number | undefined,
  maxDepth: number
): number | undefined {
  if (existingDepth !== undefined && maxDepth > 0 && existingDepth > maxDepth) {
    return undefined;
  }
  return existingDepth ?? autoSelectDepth ?? undefined;
}

/** Write the resolved employeeId (and clamped depth) into an HR source. */
export function withResolvedOrgLeaderSource(
  source: HRSourcePartSource,
  employeeId: number,
  depth: number | undefined
): HRSourcePartSource {
  return {
    ...source,
    manager: {
      ...source?.manager,
      id: employeeId,
      depth,
    },
  };
}

/**
 * Persist a resolved org leader into an HR source part so the rule card and the
 * rule editor read a single source of truth (`query.source.manager.id`) instead
 * of the transient `managerToAutoSelect` hint. No-op for non-HR parts.
 */
export function applyResolvedOrgLeaderToPart(
  part: ISourcePart,
  employeeId: number,
  maxDepth: number
): ISourcePart {
  if (part.query.type !== SourcePartType.HR) {
    return part;
  }
  const src = part.query.source as HRSourcePartSource;
  const depth = clampOrgLeaderDepth(src?.manager?.depth, part.depthToAutoSelect, maxDepth);
  const query: HRSourcePart = {
    type: SourcePartType.HR,
    source: withResolvedOrgLeaderSource(src ?? {}, employeeId, depth),
    exclusionary: part.query.exclusionary,
  };
  return { ...part, query };
}
