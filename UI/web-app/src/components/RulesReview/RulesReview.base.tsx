// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { classNamesFunction, IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import {
  RulesReviewStyleProps,
  RulesReviewStyles,
  RulesReviewProps,
} from './RulesReview.types';
import { RuleCardCarousel } from '../RuleCardCarousel';
import { SourcePart } from '../SourcePart';
import { useStrings } from '../../store/hooks';
import { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';
import { HRSourcePartSource } from '../../models/HRSourcePart';

const getClassNames = classNamesFunction<RulesReviewStyleProps, RulesReviewStyles>();

const noop = () => undefined;

// In read-only review, the only details rendered below the carousel for an HR/SqlMembership rule
// are its attribute filter rows (org leader/depth are shown on the card itself). So an HR rule with
// no attribute filter has nothing to review — skip the caret and the (otherwise empty) details panel.
// All other rule types always render reviewable details.
const hasReviewableDetails = (part: ISourcePart): boolean => {
  if (part.query.type === SourcePartType.HR) {
    return !!(part.query.source as HRSourcePartSource)?.filter?.toString().trim();
  }
  return true;
};

export const RulesReviewBase: React.FunctionComponent<RulesReviewProps> = (
  props: RulesReviewProps
) => {
  const { className, styles, parts, showHeader = true, showTitle = true } = props;
  const classNames: IProcessedStyleSet<RulesReviewStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  const [selectedPartId, setSelectedPartId] = useState<string | null>(null);

  // Default the selection to the first rule, and re-validate when the rules change.
  useEffect(() => {
    if (parts.length === 0) {
      if (selectedPartId !== null) {
        setSelectedPartId(null);
      }
      return;
    }
    const stillExists = parts.some((p) => p.id === selectedPartId);
    if (!stillExists) {
      setSelectedPartId(parts[0].id);
    }
  }, [parts, selectedPartId]);

  const selectedPart = parts.find((p) => p.id === selectedPartId);
  const selectedPartHasDetails = !!selectedPart && hasReviewableDetails(selectedPart);

  return (
    <div className={classNames.root}>
      {showHeader && (
        <div className={classNames.header}>
          {showTitle && <div className={classNames.title}>{strings.ManageMembership.labels.rulesTitle}</div>}
          <div className={classNames.description}>{strings.ManageMembership.labels.rulesDescription}</div>
        </div>
      )}
      {parts.length > 0 && (
        <>
          <RuleCardCarousel
            parts={parts}
            selectedPartId={selectedPartId ?? undefined}
            onSelectPart={setSelectedPartId}
            showCaret={selectedPartHasDetails}
          />
          {selectedPart && selectedPartHasDetails && (
            <div className={classNames.details}>
              <SourcePart
                key={selectedPart.id}
                partId={selectedPart.id}
                title={selectedPart.title}
                onDelete={noop}
                totalSourceParts={parts.length}
                query={selectedPart.query}
                part={selectedPart}
                isEditable={false}
                detailsOnly
              />
            </div>
          )}
        </>
      )}
    </div>
  );
};
