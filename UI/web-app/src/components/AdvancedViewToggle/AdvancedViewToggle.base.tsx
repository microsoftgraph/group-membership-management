// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { classNamesFunction, IProcessedStyleSet, Toggle } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { v4 as uuidv4 } from 'uuid';
import {
  AdvancedViewToggleStyleProps,
  AdvancedViewToggleStyles,
  AdvancedViewToggleProps,
} from './AdvancedViewToggle.types';
import { AppDispatch } from '../../store';
import {
  addSourcePart,
  buildCompositeQuery,
  clearSourceParts,
  getSourcePartsFromState,
  manageMembershipAdvancedViewQuery,
  manageMembershipIsAdvancedView,
  manageMembershipIsToggleEnabled,
  applyAdvancedViewQuery,
  setIsAdvancedView,
  setIsAdvancedViewReadOnly,
} from '../../store/manageMembership.slice';
import { selectIsJobTenantReader, selectIsJobTenantWriter } from '../../store/roles.slice';
import { selectOrgLeaderDataReturned } from '../../store/orgLeaderDetails.slice';
import { useStrings } from '../../store/hooks';
import { ISourcePart } from '../../models/ISourcePart';
import { SyncJobQuery } from '../../models/SyncJobQuery';

const getClassNames = classNamesFunction<AdvancedViewToggleStyleProps, AdvancedViewToggleStyles>();

export const AdvancedViewToggleBase: React.FunctionComponent<AdvancedViewToggleProps> = (
  props: AdvancedViewToggleProps
) => {
  const { className, readOnly, styles } = props;
  const classNames: IProcessedStyleSet<AdvancedViewToggleStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();
  const dispatch = useDispatch<AppDispatch>();

  const isAdvancedView = useSelector(manageMembershipIsAdvancedView);
  const isToggleEnabled = useSelector(manageMembershipIsToggleEnabled);
  const isJobTenantWriter = useSelector(selectIsJobTenantWriter);
  const isJobTenantReader = useSelector(selectIsJobTenantReader);
  const orgLeaderDataReturned = useSelector(selectOrgLeaderDataReturned);
  const sourceParts = useSelector(getSourcePartsFromState);
  const advancedViewQuery = useSelector(manageMembershipAdvancedViewQuery) ?? '';

  // Tenant readers always get a read-only advanced view: they can inspect the JSON query
  // but the toggle must never rewrite source parts or any other membership state. Callers
  // can force read-only for writers too, but can never opt a non-writer out of it.
  const isReadOnly = !isJobTenantWriter || readOnly === true;

  if (!isJobTenantWriter && !isJobTenantReader) return null;

  const handleToggleChange = () => {
    const newIsAdvancedView = !isAdvancedView;

    if (isReadOnly) {
      // Read-only: only project the current source parts into JSON for display.
      dispatch(setIsAdvancedViewReadOnly(newIsAdvancedView));
      return;
    }

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

  return (
    <div className={classNames.root}>
      <Toggle
        id="advancedViewToggle"
        inlineLabel
        onText={strings.ManageMembership.labels.advancedView}
        offText={strings.ManageMembership.labels.advancedView}
        onChange={handleToggleChange}
        checked={isAdvancedView}
        disabled={isReadOnly ? false : (!isToggleEnabled || orgLeaderDataReturned === false)}
      />
    </div>
  );
};
