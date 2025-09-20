// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface HRTitleGeneratorParams {
  orgLeaderName: string;
  depth?: number;
}

export interface HRTitleTemplates {
  orgLeaderTitle?: string;
  orgLeaderSingleLevelTitle?: string;
  orgLeaderMultipleLevelsTitle?: string;
  withSummarizedCriteria?: string;
}

/**
 * Generates a human-readable title for HR organizational queries based on leader name and depth
 * @param orgLeaderName - The name of the organizational leader
 * @param depth - The organizational depth (optional)
 * @param templates - Optional localized templates for title formatting
 * @returns A formatted title string
 */
export const generateHRTitle = (
  { orgLeaderName, depth }: HRTitleGeneratorParams,
  templates?: HRTitleTemplates
): string => {
  const defaultTemplates = {
    orgLeaderTitle: `Everyone in {0}'s org`,
    orgLeaderSingleLevelTitle: `{0} level of direct reports of {1}`,
    orgLeaderMultipleLevelsTitle: `{0} levels of direct reports of {1}`
  };

  const finalTemplates = { ...defaultTemplates, ...templates };

  if (depth && depth > 0) {
    const levels = (depth ?? 1) - 1;
    if (levels === 1) {
      return finalTemplates.orgLeaderSingleLevelTitle
        .replace('{0}', levels.toString())
        .replace('{1}', orgLeaderName);
    } else if (levels > 1) {
      return finalTemplates.orgLeaderMultipleLevelsTitle
        .replace('{0}', levels.toString())
        .replace('{1}', orgLeaderName);
    }
  }

  return finalTemplates.orgLeaderTitle.replace('{0}', orgLeaderName);
};

/**
 * Extracts organizational leader name from existing HR title strings
 * @param title - The existing title string
 * @returns The extracted leader name or empty string if not found
 */
export const extractOrgLeaderName = (title: string): string => {
  // Match "Everyone in [Name]'s org" pattern
  const orgLeaderNameMatch = title.match(/Everyone in (.*)'s org/);
  if (orgLeaderNameMatch && orgLeaderNameMatch[1]) {
    return orgLeaderNameMatch[1];
  }

  // Match "X level(s) of direct reports of [Name]" pattern
  const directReportsMatch = title.match(/\d+ levels? of direct reports of (.*?)(\s+with|$)/);
  if (directReportsMatch && directReportsMatch[1]) {
    return directReportsMatch[1].trim();
  }

  return "";
};

/**
 * Extracts depth information from existing HR title strings
 * @param title - The existing title string
 * @returns The extracted depth or undefined if not found
 */
export const extractDepthFromTitle = (title: string): number | undefined => {
  // Match "X level(s) of direct reports" pattern
  const depthMatch = title.match(/(\d+) levels? of direct reports/);
  if (depthMatch && depthMatch[1]) {
    const levels = parseInt(depthMatch[1]);
    return levels + 1; // Convert levels back to depth
  }
  return undefined;
};

/**
 * Extracts the criteria part from HR title (everything after the criteria separator)
 * @param title - The existing title string
 * @param criteriaSeparator - The localized criteria separator text (defaults to English)
 * @returns The criteria part or empty string if not found
 */
export const extractCriteriaFromTitle = (
  title: string, 
  criteriaSeparator: string = "with the following summarized criteria:"
): string => {
  const escapedSeparator = criteriaSeparator.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const criteriaMatch = title.match(new RegExp(`${escapedSeparator} (.+)$`));
  return criteriaMatch ? criteriaMatch[1] : "";
};

/**
 * Updates an existing HR title with a new organizational leader name while preserving depth and criteria
 * @param currentTitle - The existing title string
 * @param newOrgLeaderName - The new organizational leader name
 * @param templates - Optional localized templates for title formatting
 * @returns The updated title string with new leader name
 */
export const updateHRTitleWithNewLeader = (
  currentTitle: string,
  newOrgLeaderName: string,
  templates?: HRTitleTemplates
): string => {
  if (!currentTitle.trim()) {
    // If no current title, generate a new one
    return generateHRTitle({ orgLeaderName: newOrgLeaderName }, templates);
  }

  // Extract depth and criteria from current title
  const currentDepth = extractDepthFromTitle(currentTitle);
  const criteriaSeparator = templates?.withSummarizedCriteria || "with the following summarized criteria:";
  const criteria = extractCriteriaFromTitle(currentTitle, criteriaSeparator);
  
  // Generate new title with the same depth structure
  let newTitle = generateHRTitle({ 
    orgLeaderName: newOrgLeaderName, 
    depth: currentDepth 
  }, templates);

  // Append criteria if it exists
  if (criteria) {
    newTitle += ` ${criteriaSeparator} ${criteria}`;
  }

  return newTitle;
};

/**
 * Updates an existing HR title with a new depth while preserving leader name and criteria
 * @param currentTitle - The existing title string
 * @param newDepth - The new depth value
 * @param templates - Optional localized templates for title formatting
 * @returns The updated title string with new depth
 */
export const updateHRTitleWithNewDepth = (
  currentTitle: string,
  newDepth: number | undefined,
  templates?: HRTitleTemplates
): string => {
  if (!currentTitle.trim()) {
    // If no current title, can't update depth without leader name
    return currentTitle;
  }

  // Extract leader name and criteria from current title
  const currentLeaderName = extractOrgLeaderName(currentTitle);
  const criteriaSeparator = templates?.withSummarizedCriteria || "with the following summarized criteria:";
  const criteria = extractCriteriaFromTitle(currentTitle, criteriaSeparator);
  
  if (!currentLeaderName) {
    // Can't update depth without knowing the leader name
    return currentTitle;
  }

  // Generate new title with updated depth
  let newTitle = generateHRTitle({ 
    orgLeaderName: currentLeaderName, 
    depth: newDepth 
  }, templates);

  // Append criteria if it exists
  if (criteria) {
    newTitle += ` ${criteriaSeparator} ${criteria}`;
  }

  return newTitle;
};

/**
 * Combines HR organizational titles with AI-generated criteria titles intelligently
 * @param existingTitle - The current part title (could be empty, org title, or already combined)
 * @param aiTitle - The AI-generated title/criteria
 * @param isHRWithManager - Whether this is an HR part with a manager (organizational query)
 * @param criteriaSeparator - The localized criteria separator text
 * @returns The combined title following business rules
 */
export const combineHRTitleWithAICriteria = (
  existingTitle: string,
  aiTitle: string | undefined,
  isHRWithManager: boolean,
  criteriaSeparator: string = "with the following summarized criteria:"
): string => {
  // If no AI title available, return existing title
  if (!aiTitle) {
    return existingTitle;
  }

  // If no existing title, use AI title only
  if (!existingTitle || existingTitle === "") {
    return aiTitle;
  }

  // If this is an HR part with manager and doesn't already have criteria
  if (isHRWithManager && !existingTitle.includes(criteriaSeparator)) {
    return `${existingTitle} ${criteriaSeparator} ${aiTitle}`;
  }

  // Otherwise, keep existing title unchanged
  return existingTitle;
};

export interface GroupTitleTemplates {
  allUsersInGroup?: string;
  allUsersInFallback?: string;
}

/**
 * Generates a human-readable title for group membership queries
 * @param groupName - The name of the group (from search results)
 * @param fallbackSource - The source ID to use if group name is not available
 * @param templates - Optional localized templates for title formatting
 * @returns A formatted title string for group membership
 */
export const generateGroupTitle = (
  groupName: string | undefined,
  fallbackSource?: string,
  templates?: GroupTitleTemplates
): string => {
  const defaultTemplates = {
    allUsersInGroup: 'All Users in {0}',
    allUsersInFallback: 'All Users in Group'
  };

  const finalTemplates = { ...defaultTemplates, ...templates };

  // If we have a group name, use it in the standard format
  if (groupName && groupName.trim()) {
    return finalTemplates.allUsersInGroup.replace('{0}', groupName);
  }

  // If no group name but we have a source, use it as fallback with same template
  if (fallbackSource && fallbackSource.trim()) {
    return finalTemplates.allUsersInGroup.replace('{0}', fallbackSource);
  }

  // Default fallback
  return finalTemplates.allUsersInFallback;
};