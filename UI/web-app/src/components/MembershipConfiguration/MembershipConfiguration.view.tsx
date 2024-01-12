// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect } from 'react';
import { classNamesFunction, DefaultButton, IProcessedStyleSet, Toggle } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { MembershipConfigurationStyleProps, MembershipConfigurationStyles, MembershipConfigurationViewProps } from './MembershipConfiguration.types';
import { useStrings } from "../../store/hooks";
import { AdvancedQuery } from '../AdvancedQuery';
import { useDispatch, useSelector } from 'react-redux';
import { AppDispatch } from '../../store';
import { addSourcePart, buildCompositeQuery, deleteSourcePart, getSourcePartsFromState, manageMembershipIsAdvancedView, manageMembershipQuery, setCompositeQuery, setIsAdvancedView, setIsQueryValid, setNewJobQuery, updateSourcePart, updateSourcePartValidity } from '../../store/manageMembership.slice';
import { SourcePart } from '../SourcePart';

const getClassNames = classNamesFunction<MembershipConfigurationStyleProps, MembershipConfigurationStyles>();


export const MembershipConfigurationView: React.FunctionComponent<MembershipConfigurationViewProps> = (props: MembershipConfigurationViewProps) => {
  const { className, styles } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<MembershipConfigurationStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const dispatch = useDispatch<AppDispatch>();


  const isAdvancedView = useSelector(manageMembershipIsAdvancedView);
  const sourceParts = useSelector(getSourcePartsFromState);
  const globalQuery = useSelector(manageMembershipQuery);

  const placeholderQueryHRPart: string = `{
    "type": "SqlMembership",
    "source": {
      "ids": [],
      "filter": "",
      "depth": 1
    }
  }`;

  const newSourcePart = () => {
    const newPartId = sourceParts.length > 0 ? Math.max(...sourceParts.map(part => part.id)) + 1 : 1;
    const newPart = {
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
    dispatch(setIsAdvancedView(!isAdvancedView));
  };

  const handleQueryChange = (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, partId: number, newValue?: string) => {
    dispatch(updateSourcePart({ id: partId, query: newValue || '', isValid: false, isExclusionary: false }));
  };

  const handleGlobalQueryChange = (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: string) => {
    dispatch(setNewJobQuery(newValue || ''));
    dispatch(setIsQueryValid(false));
  };

  const createHandleQueryChange = (partId: number) => (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: string) => {
    handleQueryChange(event, partId, newValue);
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
              onQueryChange={createHandleQueryChange(part.id)}
            />
          ))}
        </div>
        <div className={classNames.addButtonContainer}>
          <DefaultButton iconProps={{ iconName: 'Add' }} onClick={newSourcePart}  >
            Add source
          </DefaultButton>
        </div>
      </>) : (<div className={classNames.card}>
        <AdvancedQuery query={globalQuery} onQueryChange={handleGlobalQueryChange} partId={1} onValidate={handleValidationResult}/>
      </div>
      )}
    </div>
  );
}