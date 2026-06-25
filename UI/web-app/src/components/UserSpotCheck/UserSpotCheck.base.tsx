// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  classNamesFunction,
  type IProcessedStyleSet,
  type IPersonaProps,
  Label,
  MessageBar,
  MessageBarType,
  NormalPeoplePicker,
  Spinner,
  SpinnerSize,
  TooltipHost,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import type {
  UserSpotCheckProps,
  UserSpotCheckStyleProps,
  UserSpotCheckStyles,
} from './UserSpotCheck.types';
import { useStrings } from '../../store/hooks';
import { AppDispatch } from '../../store';
import { getPeoplePickerSuggestions } from '../../store/jobs.api';
import { spotCheckUser } from '../../store/spotCheck.api';
import {
  clearSpotCheck,
  selectSpotCheckError,
  selectSpotCheckResult,
  selectSpotCheckStatus,
} from '../../store/spotCheck.slice';
import { SpotCheckPartResult } from '../../models/SpotCheckResult';

export const getClassNames = classNamesFunction<UserSpotCheckStyleProps, UserSpotCheckStyles>();

export const UserSpotCheckBase: React.FunctionComponent<UserSpotCheckProps> = (props: UserSpotCheckProps) => {
  const { className, styles, syncJobId, sourceParts } = props;
  const classNames: IProcessedStyleSet<UserSpotCheckStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();
  const spotCheckStrings = strings.Components.UserSpotCheck;
  const dispatch = useDispatch<AppDispatch>();

  const status = useSelector(selectSpotCheckStatus);
  const result = useSelector(selectSpotCheckResult);
  const error = useSelector(selectSpotCheckError);

  useEffect(() => {
    dispatch(clearSpotCheck());
    return () => {
      dispatch(clearSpotCheck());
    };
  }, [dispatch, syncJobId]);

  const onResolveSuggestions = useCallback(
    async (filterText: string): Promise<IPersonaProps[]> => {
      const trimmed = filterText?.trim();
      if (!trimmed) {
        return [];
      }
      try {
        const users = await dispatch(getPeoplePickerSuggestions(trimmed)).unwrap();
        return users.map((user) => ({
          key: user.id,
          id: user.id,
          text: user.text,
          secondaryText: user.secondaryText,
        }));
      } catch {
        return [];
      }
    },
    [dispatch]
  );

  const handlePickerChange = useCallback(
    (items?: IPersonaProps[]): void => {
      if (items && items.length > 0 && items[0].id) {
        dispatch(spotCheckUser({ syncJobId, userId: items[0].id }));
      } else {
        dispatch(clearSpotCheck());
      }
    },
    [dispatch, syncJobId]
  );

  const getPartTitle = useCallback(
    (part: SpotCheckPartResult): string => {
      const matched = sourceParts[part.index];
      if (matched && matched.title) {
        return matched.title;
      }
      return `${spotCheckStrings.sourcePartLabel} ${part.index + 1}`;
    },
    [sourceParts, spotCheckStrings.sourcePartLabel]
  );

  const getStatusDisplay = useCallback(
    (part: SpotCheckPartResult): { icon: string; label: string; tooltip: string } => {
      if (!part.supported) {
        return {
          icon: '\u26A0\uFE0F',
          label: spotCheckStrings.notSupported,
          tooltip: spotCheckStrings.notSupportedTooltip,
        };
      }
      if (part.included === null || part.included === undefined) {
        return {
          icon: '\u2754',
          label: spotCheckStrings.undetermined,
          tooltip: spotCheckStrings.undeterminedTooltip,
        };
      }
      const included = part.included === true;
      if (part.exclusionary) {
        return included
          ? {
              icon: '\u2757\u2705',
              label: spotCheckStrings.exclusionaryIncluded,
              tooltip: spotCheckStrings.exclusionaryIncludedTooltip,
            }
          : {
              icon: '\u2757\u274C',
              label: spotCheckStrings.exclusionaryNotIncluded,
              tooltip: spotCheckStrings.exclusionaryNotIncludedTooltip,
            };
      }
      return included
        ? {
            icon: '\u2705',
            label: spotCheckStrings.included,
            tooltip: spotCheckStrings.includedTooltip,
          }
        : {
            icon: '\u274C',
            label: spotCheckStrings.notIncluded,
            tooltip: spotCheckStrings.notIncludedTooltip,
          };
    },
    [spotCheckStrings]
  );

  return (
    <div className={classNames.root}>
      <div className={classNames.title}>{spotCheckStrings.title}</div>
      <div className={classNames.description}>{spotCheckStrings.description}</div>
      <Label htmlFor="userSpotCheckPicker">{spotCheckStrings.searchLabel}</Label>
      <NormalPeoplePicker
        inputProps={{ id: 'userSpotCheckPicker', 'aria-label': spotCheckStrings.searchLabel }}
        onResolveSuggestions={onResolveSuggestions}
        pickerSuggestionsProps={{
          suggestionsHeaderText: spotCheckStrings.suggestedText,
          noResultsFoundText: spotCheckStrings.noResultsFoundText,
          loadingText: spotCheckStrings.loadingText,
        }}
        key="userSpotCheck"
        selectionAriaLabel={spotCheckStrings.selectionAriaLabel}
        removeButtonAriaLabel={spotCheckStrings.removeButtonAriaLabel}
        resolveDelay={600}
        itemLimit={1}
        onChange={handlePickerChange}
        styles={{ text: classNames.picker }}
        pickerCalloutProps={{ calloutMinWidth: 400 }}
      />

      {status === 'loading' && (
        <div className={classNames.spinnerContainer}>
          <Spinner size={SpinnerSize.small} />
          <span>{spotCheckStrings.checking}</span>
        </div>
      )}

      {status === 'failed' && (
        <MessageBar className={classNames.errorMessage} messageBarType={MessageBarType.error}>
          {spotCheckStrings.errorMessage}
        </MessageBar>
      )}

      {status === 'succeeded' && result && !result.accountEnabled && (
        <MessageBar className={classNames.errorMessage} messageBarType={MessageBarType.error}>
          {spotCheckStrings.accountDisabled}
        </MessageBar>
      )}

      {status === 'succeeded' && result && result.accountEnabled && (
        <>
          <div className={classNames.resultsContainer}>
            {result.parts.map((part) => {
              const display = getStatusDisplay(part);
              return (
                <div className={classNames.partRow} key={part.index}>
                  <span className={classNames.partTitle}>{getPartTitle(part)}</span>
                  <TooltipHost content={display.tooltip}>
                    <span className={classNames.statusBadge}>
                      <span className={classNames.statusIcon} aria-hidden="true">
                        {display.icon}
                      </span>
                      <span>{display.label}</span>
                    </span>
                  </TooltipHost>
                </div>
              );
            })}
          </div>
          {result.hasUnsupportedParts && (
            <MessageBar className={classNames.banner} messageBarType={MessageBarType.warning}>
              {spotCheckStrings.unsupportedBanner}
            </MessageBar>
          )}
        </>
      )}
    </div>
  );
};
