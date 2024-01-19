// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { classNamesFunction, DefaultButton, IProcessedStyleSet, Toggle } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { MembershipConfigurationStyleProps, MembershipConfigurationStyles, MembershipConfigurationProps } from './MembershipConfiguration.types';
import { AdvancedQuery } from '../AdvancedQuery';
import { AppDispatch } from '../../store';
import {
  addSourcePart,
  buildCompositeQuery,
  clearSourceParts,
  deleteSourcePart,
  getSourcePartsFromState,
  ISourcePart,
  manageMembershipAdvancedViewQuery,
  manageMembershipIsAdvancedView,
  manageMembershipIsToggleEnabled,
  manageMembershipQuery,
  placeholderAdvancedViewQuery,
  setAdvancedViewQuery,
  setCompositeQuery,
  setIsAdvancedView,
  setIsQueryValid,
  updateSourcePart,
  updateSourcePartValidity
} from '../../store/manageMembership.slice';
import { SourcePart } from '../SourcePart';
import { useStrings } from '../../store/hooks';
import { HRSourcePart } from '../../models/HRSourcePart';
import { SyncJobQuery } from '../../models/SyncJobQuery';

const getClassNames = classNamesFunction<MembershipConfigurationStyleProps, MembershipConfigurationStyles>();

export const MembershipConfigurationBase: React.FunctionComponent<MembershipConfigurationProps> = (props: MembershipConfigurationProps) => {
  const { className, styles } = props;
  const classNames: IProcessedStyleSet<MembershipConfigurationStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const dispatch = useDispatch<AppDispatch>();
  const strings = useStrings();

  const isAdvancedView = useSelector(manageMembershipIsAdvancedView);
  const sourceParts = useSelector(getSourcePartsFromState);

  const globalQuery = useSelector(manageMembershipQuery);
  const advancedViewQuery = useSelector(manageMembershipAdvancedViewQuery) ?? globalQuery;
  const isToggleEnabled = useSelector(manageMembershipIsToggleEnabled);

  const placeholderQueryHRPart: HRSourcePart = {
    type: "SqlMembership",
    source: {
      ids: [],
      filter: "",
      depth: 1
    },
    exclusionary: false
  };

  const newSourcePart = () => {
    const newPartId = sourceParts.length > 0 ? Math.max(...sourceParts.map(part => part.id)) + 1 : 1;
    const newPart: ISourcePart = {
      id: newPartId,
      query: placeholderQueryHRPart,
      isValid: false,
      isExclusionary: false
    };
    dispatch(addSourcePart(newPart));
  };

  const removeSourcePart = (partId: number) => {
    dispatch(deleteSourcePart(partId));
  };

  const handleToggleChange = () => {
    const newIsAdvancedView = !isAdvancedView;

    if (newIsAdvancedView) {
      if (!(sourceParts.length === 0)) {
        const currentCompositeQuery = buildCompositeQuery(sourceParts);
        dispatch(setAdvancedViewQuery(currentCompositeQuery));
      }
    } else {
      // When switching back to non-advanced view
      if (advancedViewQuery !== placeholderAdvancedViewQuery) {
        try {
          const updatedSourceParts: ISourcePart[] = advancedViewQuery.map((part: ISourcePart, index: number) => ({
            id: index + 1,
            query: JSON.stringify(part, null, 2),
            isValid: true,
            isExclusionary: part.isExclusionary
          }));
          dispatch(clearSourceParts());
          updatedSourceParts.forEach(part => dispatch(addSourcePart(part)));
        } catch (error) {
          console.error(`Error parsing advanced view query:`, error);
        }
      }
    }

    dispatch(setIsAdvancedView(newIsAdvancedView));
  };

  const handleSourcePartQueryChange = (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, part: ISourcePart, newValue?: string) => {
    let newQuery;
    try {
      newQuery = typeof newValue === 'string' ? JSON.parse(newValue) : newValue;

      dispatch(updateSourcePart({
        id: part.id,
        query: newQuery,
        isValid: false,
        isExclusionary: part.isExclusionary
      }));
    } catch (error) {
      console.error('Error parsing new HRSourcePart:', error);
    }
  };

  const handleAdvancedViewQueryChange = (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: SyncJobQuery) => {
    dispatch(setAdvancedViewQuery(newValue));
    dispatch(setIsQueryValid(false));
  };

  const createHandleQueryChange = (part: ISourcePart) =>
    (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: HRSourcePart) => {
      let newQueryValue: string;
      if (typeof newValue === 'string') {
        newQueryValue = newValue;
      } else if (newValue && typeof newValue === 'object') {
        newQueryValue = JSON.stringify(newValue, null, 2);
      } else {
        newQueryValue = '';
      }
      handleSourcePartQueryChange(event, part, newQueryValue);
    };

  const handleValidationResult = (isValid: boolean, partId: number) => {
    dispatch(updateSourcePartValidity({ partId: partId, isValid }));
  };

  useEffect(() => {
    const compositeQuery = buildCompositeQuery(sourceParts);
    dispatch(setCompositeQuery(compositeQuery));
  }, [dispatch, sourceParts]);


  return (
    <div>
      <div className={classNames.toggleContainer}>
        <Toggle
          inlineLabel
          onText={strings.ManageMembership.labels.advancedView}
          offText={strings.ManageMembership.labels.advancedView}
          onChange={handleToggleChange}
          checked={isAdvancedView}
          disabled={!isToggleEnabled}
        />
      </div>
      {!isAdvancedView ? (<>
        <div>
          {sourceParts.map((part, arrayIndex) => (
            <SourcePart
              key={part.id}
              index={arrayIndex + 1}
              onDelete={removeSourcePart}
              totalSourceParts={sourceParts.length}
              query={part.query}
              part={part}
              onQueryChange={createHandleQueryChange(part)}
            />
          ))}
        </div>
        <div className={classNames.addButtonContainer}>
          <DefaultButton iconProps={{ iconName: 'Add' }} onClick={newSourcePart}  >
            {strings.ManageMembership.labels.addSourcePart}
          </DefaultButton>
        </div>
      </>) : (<div className={classNames.card}>
        <AdvancedQuery
          query={advancedViewQuery}
          onQueryChange={handleAdvancedViewQueryChange}
          partId={1}
          onValidate={handleValidationResult}
        />
      </div>
      )}
    </div>
  );
};
