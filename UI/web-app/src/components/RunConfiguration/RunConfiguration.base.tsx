// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import {
  IProcessedStyleSet,
  classNamesFunction,
  useTheme,
  ChoiceGroup, IChoiceGroupOption, DatePicker, Dropdown, Checkbox
} from '@fluentui/react';
import {
  IRunConfigurationProps,
  IRunConfigurationStyleProps,
  IRunConfigurationStyles,
} from './RunConfiguration.types';
import { useStrings } from "../../store/hooks";
import { InfoLabel } from '../InfoLabel';
import {
  manageMembershipPeriod,
  manageMembershipShowDecreaseDropdown,
  manageMembershipShowIncreaseDropdown,
  manageMembershipStartDate,
  manageMembershipStartDateOption,
  manageMembershipThresholdPercentageForAdditions,
  manageMembershipThresholdPercentageForRemovals,
  manageMembershipUseThresholdLimits,
  setNewJobPeriod,
  setNewJobStartDate,
  setNewJobThresholdPercentageForAdditions,
  setNewJobThresholdPercentageForRemovals,
  setShowDecreaseDropdown,
  setShowIncreaseDropdown,
  setStartDateOption,
  setUseThresholdLimits
} from '../../store/manageMembership.slice';
import { AppDispatch } from '../../store';
import { useDispatch, useSelector } from 'react-redux';

const getClassNames = classNamesFunction<
  IRunConfigurationStyleProps,
  IRunConfigurationStyles
>();

export const RunConfigurationBase: React.FunctionComponent<IRunConfigurationProps> = (props) => {
  const { className, styles } = props;
  const strings = useStrings();
  const classNames: IProcessedStyleSet<IRunConfigurationStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );
  const dispatch = useDispatch<AppDispatch>();
  const period: number = useSelector(manageMembershipPeriod);
  const startDate: string = useSelector(manageMembershipStartDate);
  const thresholdPercentageForAdditions: number = useSelector(manageMembershipThresholdPercentageForAdditions);
  const thresholdPercentageForRemovals: number = useSelector(manageMembershipThresholdPercentageForRemovals);

  const useThresholdLimits = useSelector(manageMembershipUseThresholdLimits);
  const startDateOption = useSelector(manageMembershipStartDateOption);
  const showIncreaseDropdown = useSelector(manageMembershipShowIncreaseDropdown);
  const showDecreaseDropdown = useSelector(manageMembershipShowDecreaseDropdown);

  const defaultIncreaseThreshold: number = 100;
  const defaultDecreaseThreshold: number = 20;

  const startDateOptions: IChoiceGroupOption[] = [
    { key: 'ASAP', text: strings.ManageMembership.labels.ASAP },
    { key: 'RequestedDate', text: strings.ManageMembership.labels.requestedDate },
  ];

  const yesNoOptions: IChoiceGroupOption[] = [
    { key: 'Yes', text: strings.yes },
    { key: 'No', text: strings.no },
  ];

  const frequencyOptions = [
    { key: '12', text: `12 ${strings.ManageMembership.labels.hrs}` },
    { key: '24', text: `24 ${strings.ManageMembership.labels.hrs}` },
    { key: '36', text: `36 ${strings.ManageMembership.labels.hrs}` }
  ];

  const increaseOptions = Array.from({ length: 10 }, (_, i) => ({ key: `${(i + 1) * 10}`, text: `${(i + 1) * 10}%` }));
  const decreaseOptions = Array.from({ length: 11 }, (_, i) => ({ key: `${i * 5}`, text: `${i * 5}%` }));

  return (
    <div className={classNames.root}>
      <ChoiceGroup
        styles={{
          root: classNames.horizontalChoiceGroup,
          flexContainer: classNames.horizontalChoiceGroupContainer
        }}
        label={strings.ManageMembership.labels.selectStartDate}
        selectedKey={startDateOption}
        options={startDateOptions}
        onChange={(ev, option) => {
          if (option) {
            dispatch(setStartDateOption(option.key));
            if (option.key === 'ASAP') {
              dispatch(setNewJobStartDate(new Date(Date.now()).toDateString()));
            }
          }
        }}
      />
      {startDateOption === 'RequestedDate' && (
        <DatePicker
          className={classNames.controlWidth}
          label={strings.ManageMembership.labels.from}
          placeholder={strings.ManageMembership.labels.selectRequestedStartDate}
          ariaLabel={strings.ManageMembership.labels.selectRequestedStartDate}
          minDate={new Date(Date.now())}
          value={new Date(startDate)}
          onSelectDate={(date: Date | null | undefined) => {
            if (date) {
              dispatch(setNewJobStartDate(date.toDateString()));
            }
          }}
        />
      )}
      <div>
        {strings.ManageMembership.labels.selectFrequency}
        <Dropdown
          styles={{ title: classNames.dropdownTitle }}
          className={classNames.controlWidth}
          label={strings.ManageMembership.labels.frequency}
          options={frequencyOptions}
          defaultSelectedKey={period.toString()}
          onChange={(event, option) => {
            if (option) {
              dispatch(setNewJobPeriod(Number(option.key)));
            }
          }}
        />
      </div>
      <div>
        <InfoLabel
          label={strings.ManageMembership.labels.preventAutomaticSync}
          description={strings.ManageMembership.labels.preventAutomaticSync}
        />
        <ChoiceGroup
          styles={{
            root: classNames.horizontalChoiceGroup,
            flexContainer: classNames.horizontalChoiceGroupContainer
          }}
          selectedKey={useThresholdLimits}
          options={yesNoOptions}
          onChange={(ev, option) => {
            if (option) {
              dispatch(setUseThresholdLimits(option.key));
              if (option.key === 'No') {
                dispatch(setShowIncreaseDropdown(false));
                dispatch(setShowDecreaseDropdown(false));
                dispatch(setNewJobThresholdPercentageForAdditions(-1));
                dispatch(setNewJobThresholdPercentageForRemovals(-1));
              }
              if (option.key === 'Yes') {
                dispatch(setShowIncreaseDropdown(true));
                dispatch(setShowDecreaseDropdown(true));
                dispatch(setNewJobThresholdPercentageForAdditions(100));
                dispatch(setNewJobThresholdPercentageForRemovals(20));
              }
            }
          }}
        />
      </div>
      {useThresholdLimits === 'Yes' && (
        <div className={classNames.checkboxPairsContainer}>
          <div className={classNames.checkboxDropdownPair}>
            <Checkbox
              label={strings.ManageMembership.labels.increase}
              checked={showIncreaseDropdown}
              onChange={(ev, checked) => {
                dispatch((setShowIncreaseDropdown(!!checked)));
                if (!checked) {
                  dispatch(setNewJobThresholdPercentageForAdditions(defaultIncreaseThreshold));
                } else {
                  dispatch(setNewJobThresholdPercentageForAdditions(-1));
                }
              }}
            />
            <Dropdown
              styles={{ title: classNames.dropdownTitle, root: showIncreaseDropdown ? {} : { visibility: 'hidden' } }}
              className={classNames.thresholdDropdown}
              options={increaseOptions}
              selectedKey={thresholdPercentageForAdditions >= 0 ? thresholdPercentageForAdditions.toString() : undefined}
              onChange={(event, option) => {
                if (option) {
                  dispatch(setNewJobThresholdPercentageForAdditions(Number(option.key)));
                }
              }}
            />
          </div>

          <div className={classNames.checkboxDropdownPair}>
            <Checkbox
              label={strings.ManageMembership.labels.decrease}
              checked={showDecreaseDropdown}
              onChange={(ev, checked) => {
                dispatch(setShowDecreaseDropdown(!!checked));
                if (checked) {
                  dispatch(setNewJobThresholdPercentageForRemovals(defaultDecreaseThreshold));
                } else {
                  dispatch(setNewJobThresholdPercentageForRemovals(-1));
                }
              }}
            />
            <Dropdown
              styles={{ title: classNames.dropdownTitle, root: showDecreaseDropdown ? {} : { visibility: 'hidden' } }}
              className={classNames.thresholdDropdown}
              options={decreaseOptions}
              selectedKey={thresholdPercentageForRemovals >= 0 ? thresholdPercentageForRemovals.toString() : undefined}
              onChange={(event, option) => {
                if (option) {
                  dispatch(setNewJobThresholdPercentageForRemovals(Number(option.key)));
                }
              }}
            /></div>
        </div>
      )}
    </div>
  );
};
