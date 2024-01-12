// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { ChoiceGroup, classNamesFunction, Dropdown, IChoiceGroupOption, IDropdownOption, IProcessedStyleSet } from '@fluentui/react';
import { DefaultButton, IconButton } from '@fluentui/react/lib/Button';
import { useTheme } from '@fluentui/react/lib/Theme';
import { SourcePartStyleProps, SourcePartStyles, SourcePartViewProps } from './SourcePart.types';
import { AdvancedQuery } from '../AdvancedQuery';
import { useDispatch } from 'react-redux';
import { AppDispatch } from '../../store';
import { updateSourcePart } from '../../store/manageMembership.slice';

const getClassNames = classNamesFunction<SourcePartStyleProps, SourcePartStyles>();

export const SourcePartView: React.FunctionComponent<SourcePartViewProps> = (props: SourcePartViewProps) => {
  const { className, styles, index, totalSourceParts, onDelete, query, onQueryChange, part } = props;
  const classNames: IProcessedStyleSet<SourcePartStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  const toggleExpand = () => {
    setExpanded(!expanded);
  };
  const options: IChoiceGroupOption[] = [
    { key: 'Yes', text: 'Yes' },
    { key: 'No', text: 'No' },
  ];

  const sourceTypeOptions: IDropdownOption[] = [
    { key: 'HR', text: 'HR' },
    { key: 'Groups', text: 'Groups', disabled: true }
  ];

  const dispatch = useDispatch<AppDispatch>();
  const [expanded, setExpanded] = useState(false);
  const [sourceType, setSourceType] = useState<IDropdownOption>({ key: 'HR', text: 'HR' });
  const [isExclusionary, setIsExclusionary] = useState(part.isExclusionary);

  const [errorMessage, setErrorMessage] = useState<string>('');

  const onSourceTypeChanged = (event: React.FormEvent<HTMLDivElement>, item: IDropdownOption | undefined): void => {
    if (item) {
      setSourceType(item);
    }
  }

  const handleDelete = () => {
    if (totalSourceParts > 1) {
      onDelete(part.id);
    } else {
      setErrorMessage("Cannot delete the last source part.");
    }
  }

  useEffect(() => {
    setErrorMessage('');
  }, [sourceType, expanded]);

  const handleQueryValidation = (isValid: boolean, partId: number) => {
    console.log(`Query for part ${partId} is ${isValid ? 'valid' : 'invalid'}`);
  };

  const handleExclusionaryChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption): void => {
    if (!option) return;
    const isExclusionarySelected = option.key === 'Yes';
    setIsExclusionary(isExclusionarySelected);
    dispatch(updateSourcePart({ 
        id: part.id, 
        query: part.query,
        isValid: part.isValid,
        isExclusionary: isExclusionarySelected 
    }));
};

  return (
    <div className={classNames.card}>
      <div className={classNames.header}>
        <div className={classNames.title}>
          Source Part {index}
        </div>
        <IconButton
          className={classNames.expandButton}
          iconProps={{ iconName: expanded ? 'ChevronUp' : 'ChevronDown' }}
          onClick={toggleExpand}
          title="Expand/Collapse"
        />
      </div>
      {expanded &&
        <div className={classNames.content}>
          <div className={classNames.controls}>
            <Dropdown
              options={sourceTypeOptions}
              label="Source Type"
              required={true}
              selectedKey={sourceType.key}
              onChange={onSourceTypeChanged}
            />
            <ChoiceGroup
              className={classNames.exclusionaryPart}
              options={options}
              label="Exclude source part"
              required={true}
              onChange={handleExclusionaryChange}
              selectedKey={isExclusionary ? 'Yes' : 'No'}
            />
            <DefaultButton iconProps={{ iconName: 'Delete' }} className={classNames.deleteButton} onClick={handleDelete} >
              Delete
            </DefaultButton>
          </div>
          {sourceType.key === 'HR' && (
            <div className={classNames.advancedQuery}>
              <AdvancedQuery query={query} onQueryChange={onQueryChange} partId={part.id} onValidate={handleQueryValidation} />
            </div>
          )}
          <div className={classNames.error}>
            {errorMessage}
          </div>
        </div>
      }
    </div>
  );
}