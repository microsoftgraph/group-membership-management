// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  classNamesFunction,
  IProcessedStyleSet,
  DefaultButton,
  PrimaryButton,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { v4 as uuidv4 } from 'uuid';
import {
  RulesEditorStyleProps,
  RulesEditorStyles,
  RulesEditorProps,
} from './RulesEditor.types';
import { AppDispatch } from '../../store';
import {
  addSourcePart,
  deleteSourcePart,
  getSourcePartsFromState,
  insertSourcePartAfter,
} from '../../store/manageMembership.slice';
import { selectIsJobWriter } from '../../store/roles.slice';
import { RuleCardCarousel } from '../RuleCardCarousel';
import { SourcePart } from '../SourcePart';
import { useStrings } from '../../store/hooks';
import { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';
import { HRSourcePartSource } from '../../models/HRSourcePart';

const getClassNames = classNamesFunction<RulesEditorStyleProps, RulesEditorStyles>();

const createNewSourcePart = (): ISourcePart => {
  const source: HRSourcePartSource = {
    manager: { id: undefined, depth: undefined },
    filter: '',
  };
  return {
    id: uuidv4(),
    title: '',
    query: { type: SourcePartType.HR, source, exclusionary: false },
    isNew: true,
    isExpanded: true,
  };
};

export const RulesEditorBase: React.FunctionComponent<RulesEditorProps> = (
  props: RulesEditorProps
) => {
  const { className, styles, isEditable = true } = props;
  const classNames: IProcessedStyleSet<RulesEditorStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const dispatch = useDispatch<AppDispatch>();
  const strings = useStrings();
  const labels = strings.ManageMembership.labels;

  const sourceParts = useSelector(getSourcePartsFromState);
  const isJobWriter = useSelector(selectIsJobWriter);
  const canEdit = isEditable && isJobWriter;

  const [selectedPartId, setSelectedPartId] = useState<string | null>(null);

  // Default the selection to the first rule, and re-validate when the rules change.
  useEffect(() => {
    if (sourceParts.length === 0) {
      if (selectedPartId !== null) {
        setSelectedPartId(null);
      }
      return;
    }
    const stillExists = sourceParts.some((p) => p.id === selectedPartId);
    if (!stillExists) {
      setSelectedPartId(sourceParts[0].id);
    }
  }, [sourceParts, selectedPartId]);

  const selectedPart = sourceParts.find((p) => p.id === selectedPartId);

  const handleAdd = useCallback(() => {
    const newPart = createNewSourcePart();
    dispatch(addSourcePart(newPart));
    setSelectedPartId(newPart.id);
  }, [dispatch]);

  const handleDuplicate = useCallback(
    (partId: string) => {
      const source = sourceParts.find((p) => p.id === partId);
      if (!source) {
        return;
      }
      const copy: ISourcePart = {
        ...source,
        id: uuidv4(),
        // Deep clone the query so the copy does not share references with its source.
        query: JSON.parse(JSON.stringify(source.query)),
        isNew: false,
        isExpanded: true,
      };
      dispatch(insertSourcePartAfter({ afterPartId: partId, part: copy }));
      setSelectedPartId(copy.id);
    },
    [dispatch, sourceParts]
  );

  const handleDelete = useCallback(
    (partId: string) => {
      const index = sourceParts.findIndex((p) => p.id === partId);
      const remaining = sourceParts.filter((p) => p.id !== partId);

      let next = selectedPartId;
      if (partId === selectedPartId) {
        if (remaining.length === 0) {
          next = null;
        } else if (index === 0) {
          // Deleted the first rule: select the new first rule.
          next = remaining[0].id;
        } else {
          // Select the previous rule.
          next = sourceParts[index - 1].id;
        }
      } else if (!remaining.some((p) => p.id === selectedPartId)) {
        next = remaining[0]?.id ?? null;
      }

      dispatch(deleteSourcePart(partId));
      setSelectedPartId(next);
    },
    [dispatch, sourceParts, selectedPartId]
  );

  return (
    <div className={classNames.root}>
      <div className={classNames.headerBar}>
        <div className={classNames.description}>{labels.rulesDescription}</div>
        <DefaultButton
          className={classNames.addButton}
          iconProps={{ iconName: 'Add' }}
          onClick={handleAdd}
          disabled={!canEdit}
        >
          {labels.addRule}
        </DefaultButton>
      </div>

      {sourceParts.length === 0 ? (
        <div className={classNames.emptyState}>
          <div className={classNames.emptyTitle}>{labels.rulesEmptyTitle}</div>
          <div className={classNames.emptyDescription}>{labels.rulesEmptyDescription}</div>
          <PrimaryButton iconProps={{ iconName: 'Add' }} onClick={handleAdd} disabled={!canEdit}>
            {labels.addRule}
          </PrimaryButton>
        </div>
      ) : (
        <>
          <RuleCardCarousel
            parts={sourceParts}
            selectedPartId={selectedPartId ?? undefined}
            onSelectPart={setSelectedPartId}
            showActions={canEdit}
            onDuplicatePart={handleDuplicate}
            onDeletePart={handleDelete}
            showCaret={!!selectedPart}
          />
          {selectedPart && (
            <div className={classNames.details}>
              <SourcePart
                key={selectedPart.id}
                partId={selectedPart.id}
                title={selectedPart.title}
                onDelete={handleDelete}
                totalSourceParts={sourceParts.length}
                query={selectedPart.query}
                part={{ ...selectedPart, isExpanded: true }}
                isEditable={isEditable}
              />
            </div>
          )}
        </>
      )}
    </div>
  );
};
