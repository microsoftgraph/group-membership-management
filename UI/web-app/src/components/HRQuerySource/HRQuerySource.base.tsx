// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { classNamesFunction, Stack, type IProcessedStyleSet, IStackTokens, Label, IconButton, TooltipHost, ChoiceGroup, IChoiceGroupOption, SpinButton, NormalPeoplePicker, DirectionalHint } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { TextField } from '@fluentui/react/lib/TextField';
import { IPersonaProps } from '@fluentui/react/lib/Persona';
import type {
  HRQuerySourceProps,
  HRQuerySourceStyleProps,
  HRQuerySourceStyles,
} from './HRQuerySource.types';
import { useStrings } from '../../store/hooks';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { AppDispatch } from '../../store';
import { useDispatch, useSelector } from 'react-redux';
import { fetchOrgLeaderDetails } from '../../store/orgLeaderDetails.api';
import { getJobOwnerFilterSuggestions } from '../../store/jobs.api';
import { updateOrgLeaderDetails, selectOrgLeaderDetails, selectObjectIdEmployeeIdMapping } from '../../store/orgLeaderDetails.slice';
import { selectJobOwnerFilterSuggestions } from '../../store/jobs.slice';
import { manageMembershipIsEditingExistingJob } from '../../store/manageMembership.slice';

export const getClassNames = classNamesFunction<HRQuerySourceStyleProps, HRQuerySourceStyles>();

export const HRQuerySourceBase: React.FunctionComponent<HRQuerySourceProps> = (props: HRQuerySourceProps) => {
  const { className, styles, partId, onSourceChange } = props;
  const classNames: IProcessedStyleSet<HRQuerySourceStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  const stackTokens: IStackTokens = {
    childrenGap: 30
  };

  const dispatch = useDispatch<AppDispatch>();
  const orgLeaderDetails = useSelector(selectOrgLeaderDetails);
  const objectIdEmployeeIdMapping = useSelector(selectObjectIdEmployeeIdMapping);
  const ownerPickerSuggestions = useSelector(selectJobOwnerFilterSuggestions);
  const isEditingExistingJob = useSelector(manageMembershipIsEditingExistingJob);
  const [isDisabled, setIsDisabled] = useState(true);
  const [includeOrg, setIncludeOrg] = useState(false);
  const [includeFilter, setIncludeFilter] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string>('');
  const [source, setSource] = useState<HRSourcePartSource>(props.source);
  const excludeLeaderQuery = `EmployeeId != ${source.manager?.id}`

  useEffect(() => {
    setErrorMessage('');
  }, [source]);

  useEffect(() => {
    setIsDisabled(!orgLeaderDetails.maxDepth);
  }, [orgLeaderDetails.maxDepth]);

  useEffect(() => {
    if (orgLeaderDetails.employeeId > 0 && partId === orgLeaderDetails.partId) {
      const id: number = orgLeaderDetails.employeeId;
      setSource(prevSource => {
        const newSource = {
          ...prevSource,
          manager: {
            ...prevSource.manager,
            id
          }
        };
        onSourceChange(newSource, partId);
        return newSource;
      })
    }
  }, [orgLeaderDetails.employeeId, orgLeaderDetails.objectId]);

  const getPickerSuggestions = async (
    filterText: string
  ): Promise<IPersonaProps[]> => {
    return filterText && ownerPickerSuggestions ? ownerPickerSuggestions : [];
  };

  const handleOrgLeaderInputChange = (input: string): string => {
    dispatch(getJobOwnerFilterSuggestions({displayName: input, alias: input}))
    return input;
  }

  const handleOrgLeaderChange = (items?: IPersonaProps[] | undefined) => {
    setIsDisabled(true);
    if (items !== undefined && items.length > 0) {
      dispatch(fetchOrgLeaderDetails({
        objectId: items[0].id as string,
        key: items[0].key as number,
        text: items[0].text as string,
        partId: partId as number
      }))
    }
  };

  const handleDepthChange = React.useCallback((event: React.SyntheticEvent<HTMLElement>, newValue?: string) => {
    const nonNumericRegex = /[^0-9]/g;
    if (newValue && nonNumericRegex.test(newValue)) {
      setErrorMessage(strings.HROnboarding.invalidInputErrorMessage);
      return;
    }
    const depth = newValue?.trim() !== '' ? Number(newValue) : undefined;
    setSource(prevSource => {
      const newSource = {
        ...prevSource,
        manager: {
          ...prevSource.manager,
          depth
        }
      };
      onSourceChange(newSource, partId);
      return newSource;
    });
  }, []);

  const handleFilterChange = (_: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '') => {
    const filter = newValue;
    setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
    });
  };

  const yesNoOptions: IChoiceGroupOption[] = [
    { key: 'Yes', text: strings.yes },
    { key: 'No', text: strings.no }
  ];

  const handleIncludeOrgChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption) => {
    if (option?.key === "No") {
      setIncludeOrg(false);
      dispatch(updateOrgLeaderDetails({ employeeId: -1 }));
      const id = undefined;
      const depth = undefined;
      setSource(prevSource => {
        const newSource = {
          ...prevSource,
          manager: {
            ...prevSource.manager,
            id,
            depth
          }
        };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
    else if (option?.key === "Yes") {
      setIncludeOrg(true);
      setSource(prevSource => {
        const newSource = { ...prevSource};
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
  }

  const handleIncludeFilterChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption) => {
    if (option?.key === "No") {
      setIncludeFilter(false);
      const filter = "";
      setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
    else if (option?.key === "Yes") {
      setIncludeFilter(true);
      setSource(prevSource => {
        const newSource = { ...prevSource };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
  }

  const handleIncludeLeaderChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption) => {
    setErrorMessage('');
    let filter: string;
    if (option?.key === "No") {
      if (!source.manager?.id)
      {
        setErrorMessage(strings.HROnboarding.orgLeaderMissingErrorMessage);
        return;
      }
      if (source.filter !== "") {
        filter = `${source.filter} and ` + excludeLeaderQuery;
      } else {
        filter = excludeLeaderQuery;
      }
    }
    else if (option?.key === "Yes") {
      if (source.filter?.includes(excludeLeaderQuery)) {
        const regex = new RegExp(`and ${excludeLeaderQuery}|${excludeLeaderQuery} and|${excludeLeaderQuery}`, 'g');
        filter = source.filter.replace(regex, '').trim();
      }
    }
    setSource(prevSource => {
      const newSource = { ...prevSource, filter };
      onSourceChange(newSource, partId);
      return newSource;
    });
  }

  return (
    <div className={classNames.root}>
      <Label>{strings.HROnboarding.includeOrg}</Label>
      <ChoiceGroup
        selectedKey={(includeOrg || source?.manager?.id) ? strings.yes : strings.no}
        options={yesNoOptions}
        onChange={handleIncludeOrgChange}
        styles={{
          root: classNames.horizontalChoiceGroup,
          flexContainer: classNames.horizontalChoiceGroupContainer
        }}
        disabled={isEditingExistingJob}
      />

      {(includeOrg || source?.manager?.id) && (
      <Stack horizontal horizontalAlign="space-between" verticalAlign="center" tokens={stackTokens}>
        <Stack.Item align="start">
          <div>
            <div className={classNames.labelContainer}>
              <Label>{strings.HROnboarding.orgLeader}</Label>
              <TooltipHost content={strings.HROnboarding.orgLeaderInfo} id="toolTipOrgLeaderId" calloutProps={{ gapSpace: 0 }}>
                <IconButton title={strings.HROnboarding.orgLeaderInfo} iconProps={{ iconName: "Info" }} aria-describedby="toolTipOrgLeaderId" />
              </TooltipHost>
            </div>
            <NormalPeoplePicker
              aria-label={strings.HROnboarding.orgLeaderInfo}
              onResolveSuggestions={getPickerSuggestions}
              key={'normal'}
              resolveDelay={300}
              itemLimit={1}
              selectedItems={source?.manager?.id && !isDisabled ? [
                {
                  key: objectIdEmployeeIdMapping[source.manager.id]?.objectId.toString() || "",
                  text: objectIdEmployeeIdMapping[source.manager.id]?.text.toString() || ""
                },
              ] : undefined}
              onInputChange={handleOrgLeaderInputChange}
              onChange={handleOrgLeaderChange}
              styles={{ root: classNames.textField, text: classNames.textFieldGroup }}
              pickerCalloutProps={{directionalHint: DirectionalHint.bottomCenter}}
              disabled={isEditingExistingJob}
            />
          </div>
        </Stack.Item>

        <Stack.Item align="start">
          <div>
             <div className={classNames.labelContainer}>
              <Label>{strings.HROnboarding.depth}</Label>
              <TooltipHost content={strings.HROnboarding.depthInfo} id="toolTipDepthId" calloutProps={{ gapSpace: 0 }}>
                <IconButton iconProps={{ iconName: "Info" }} aria-describedby="toolTipDepthId" />
              </TooltipHost>
            </div>
            <SpinButton
              value={source.manager?.depth?.toString()}
              disabled={isDisabled || isEditingExistingJob}
              min={0}
              max={(partId === orgLeaderDetails.partId) ? orgLeaderDetails.maxDepth : 100}
              step={1}
              onChange={handleDepthChange}
              incrementButtonAriaLabel={strings.HROnboarding.incrementButtonAriaLabel}
              decrementButtonAriaLabel={strings.HROnboarding.decrementButtonAriaLabel}
              styles={{ spinButtonWrapper: classNames.spinButton }}
            />
          </div>
        </Stack.Item>

        <Stack.Item align="start">
          <div>
            <Label>{strings.HROnboarding.includeLeader}</Label>
            <ChoiceGroup
              selectedKey={(source.filter?.includes(excludeLeaderQuery))? strings.no : strings.yes}
              options={yesNoOptions}
              onChange={handleIncludeLeaderChange}
              styles={{
                root: classNames.horizontalChoiceGroup,
                flexContainer: classNames.horizontalChoiceGroupContainer
              }}
              disabled={isEditingExistingJob}
            />
          </div>
        </Stack.Item>
      </Stack>
      )}

      <Label>{strings.HROnboarding.includeFilter}</Label>
      <ChoiceGroup
        selectedKey={(includeFilter || source.filter) ? strings.yes : strings.no}
        options={yesNoOptions}
        onChange={handleIncludeFilterChange}
        styles={{
          root: classNames.horizontalChoiceGroup,
          flexContainer: classNames.horizontalChoiceGroupContainer
        }}
        disabled={isEditingExistingJob}
      />

      {(includeFilter || source.filter) && (
      <><div className={classNames.labelContainer}>
      <Label>{strings.HROnboarding.filter}</Label>
      <TooltipHost content={strings.HROnboarding.filterInfo} id="toolTipFilterId" calloutProps={{ gapSpace: 0 }}>
        <IconButton title={strings.HROnboarding.filterInfo} iconProps={{ iconName: "Info" }} aria-describedby="toolTipFilterId" />
      </TooltipHost>
      </div>
      <TextField
        placeholder={strings.HROnboarding.filterPlaceHolder}
        multiline rows={3}
        resizable={true}
        value={source.filter?.toString()}
        onChange={handleFilterChange}
        styles={{ root: classNames.textField, fieldGroup: classNames.textFieldGroup }}
        validateOnLoad={false}
        validateOnFocusOut={false}
        disabled={isEditingExistingJob}
      ></TextField></>
      )}

      <div className={classNames.error}>
        {errorMessage}
      </div>

    </div>
  );
};