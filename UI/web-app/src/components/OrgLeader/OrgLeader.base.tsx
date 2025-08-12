// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useId } from 'react';
import {
  classNamesFunction,
  type IProcessedStyleSet,
  Label,
  IconButton,
  TooltipHost,
  NormalPeoplePicker,
  DirectionalHint,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { useStrings } from '../../store/hooks';
import type {
  OrgLeaderProps,
  OrgLeaderStyleProps,
  OrgLeaderStyles,
} from './OrgLeader.types';

const getClassNames = classNamesFunction<OrgLeaderStyleProps, OrgLeaderStyles>();

export const OrgLeaderBase: React.FunctionComponent<OrgLeaderProps> = (
  props: OrgLeaderProps
) => {
  const {
    className,
    styles,
    selectedItems,
    disabled,
    onResolveSuggestions,
    onInputChange,
    onChange,
    showError,
    dataTestId = 'hr-org-leader-picker',
    calloutWidth = 300,
    tooltipId,
  } = props;

  const generatedId = useId();
  const actualTooltipId = tooltipId || `toolTipOrgLeader-${generatedId}`;

  const strings = useStrings();
  const classNames: IProcessedStyleSet<OrgLeaderStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  return (
    <div className={classNames.root}>
      <div className={classNames.labelContainer}>
        <Label>{strings.HROnboarding.provideOrgLeader}</Label>
        <TooltipHost content={strings.HROnboarding.orgLeaderInfo} id={actualTooltipId} calloutProps={{ gapSpace: 0 }}>
          <IconButton title={strings.HROnboarding.orgLeaderInfo} iconProps={{ iconName: 'Info' }} aria-describedby={actualTooltipId} />
        </TooltipHost>
      </div>
      <NormalPeoplePicker
        data-testid={dataTestId}
        aria-label={strings.HROnboarding.orgLeaderInfo}
        onResolveSuggestions={onResolveSuggestions}
        key={'normal'}
        resolveDelay={300}
        itemLimit={1}
        selectedItems={selectedItems}
        onInputChange={onInputChange}
        onChange={onChange}
        styles={{ root: classNames.textField, text: classNames.textFieldGroup }}
        pickerCalloutProps={{ directionalHint: DirectionalHint.bottomAutoEdge, calloutWidth }}
        disabled={disabled}
        pickerSuggestionsProps={{ className: classNames.suggestionItems }}
      />
      {showError && (
        <div className={classNames.error}>
          {`${strings.HROnboarding.orgLeader} ${strings.HROnboarding.orgLeaderMissingErrorMessage}`}
        </div>
      )}
    </div>
  );
};
