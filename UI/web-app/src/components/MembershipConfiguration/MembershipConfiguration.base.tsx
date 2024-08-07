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
  manageMembershipAdvancedViewQuery,
  manageMembershipCompositeQuery,
  manageMembershipIsAdvancedView,
  manageMembershipIsToggleEnabled,
  manageMembershipQuery,
  setAdvancedViewQuery,
  setCompositeQuery,
  setIsAdvancedView,
  setIsAdvancedQueryValid,
} from '../../store/manageMembership.slice';
import { SourcePart } from '../SourcePart';
import { useStrings } from '../../store/hooks';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';

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
  const advancedViewQuery = useSelector(manageMembershipAdvancedViewQuery) ?? '';
  const compositeQuery = useSelector(manageMembershipCompositeQuery) ?? globalQuery;
  const isToggleEnabled = useSelector(manageMembershipIsToggleEnabled);

  const sourcePartQuery: HRSourcePartSource = {
    manager: {
      id: undefined,
      depth: undefined
    },
    filter: ""
  };

  const newSourcePart = () => {
    const newPart: ISourcePart = {
      id: sourceParts.length + 1,
      query: {
        type: SourcePartType.HR,
        source: sourcePartQuery,
        exclusionary: false
      }
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
        dispatch(setAdvancedViewQuery(JSON.stringify(currentCompositeQuery)));
      }
    } else {
      // When switching back to non-advanced view
      if (compositeQuery) {
        try {
          const updatedSourceParts: ISourcePart[] = compositeQuery.map((query, index) => {
            const newPart: ISourcePart = {
              id: index + 1,
              query: {
                type: SourcePartType.HR,
                source: sourcePartQuery,
                exclusionary: false
              }
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
            />
          ))}
        </div>
          <div className={classNames.addButtonContainer}>
            <DefaultButton
              iconProps={{ iconName: 'Add' }}
              onClick={newSourcePart}>
              {strings.ManageMembership.labels.addSourcePart}
            </DefaultButton>
          </div>
      </>) : (<div className={classNames.card}>
        <AdvancedQuery
          query={advancedViewQuery}
          onQueryChange={handleAdvancedViewQueryChange}
          partId={1}
        />
      </div>
      )}
    </div>
  );
};
