// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { classNamesFunction, Stack, type IProcessedStyleSet, IStackTokens, Label, IconButton, TooltipHost, ChoiceGroup, IChoiceGroupOption, SpinButton } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { TextField } from '@fluentui/react/lib/TextField';
import type {
  HRQuerySourceProps,
  HRQuerySourceStyleProps,
  HRQuerySourceStyles,
} from './HRQuerySource.types';
import { useStrings } from '../../store/hooks';
import { HRSourcePartSource } from '../../models/HRSourcePart';

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

  const [errorMessage, setErrorMessage] = useState<string>('');
  const [source, setSource] = useState<HRSourcePartSource>(props.source);
  const excludeLeaderQuery = `EmployeeId != ${source.ids?.[0]}`

  useEffect(() => {
    setErrorMessage('');
  }, [source]);

  const handleOrgLeaderIdChange = (_: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '') => {
    const nonNumericRegex = /[^0-9]/g;
    if (nonNumericRegex.test(newValue)) {
      setErrorMessage(strings.HROnboarding.invalidInputErrorMessage);
      return;
    }
    const ids = newValue.trim() !== '' ? [Number(newValue)] : [];
    setSource(prevSource => {
        const newSource = { ...prevSource, ids };
        onSourceChange(newSource, partId);
        return newSource;
    });
  };

  const handleDepthChange = React.useCallback((event: React.SyntheticEvent<HTMLElement>, newValue?: string) => {
    const nonNumericRegex = /[^0-9]/g;
    if (newValue && nonNumericRegex.test(newValue)) {
      setErrorMessage(strings.HROnboarding.invalidInputErrorMessage);
      return;
    }
    const depth = newValue?.trim() !== '' ? Number(newValue) : undefined;
    setSource(prevSource => {
        const newSource = { ...prevSource, depth };
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
    const includeOrg = option?.key === "Yes";
    if (option?.key === "No") {
      const ids: number[] = [];
      const depth = undefined;
      setSource(prevSource => {
        const newSource = { ...prevSource, includeOrg, ids, depth };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
    else if (option?.key === "Yes") {
      setSource(prevSource => {
        const newSource = { ...prevSource, includeOrg };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }  
  }

  const handleIncludeFilterChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption) => {
    const includeFilter = option?.key === "Yes";
    if (option?.key === "No") {
      const filter = "";
      setSource(prevSource => {
        const newSource = { ...prevSource, filter, includeFilter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }
    else if (option?.key === "Yes") {
      setSource(prevSource => {
        const newSource = { ...prevSource, includeFilter };
        onSourceChange(newSource, partId);
        return newSource;
      });
    }    
  }

  const handleIncludeLeaderChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption) => {
    setErrorMessage('');
    let filter: string;
    if (option?.key === "No") {
      if (!source.ids || source.ids.length === 0)
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
    <div>
      <ChoiceGroup
        selectedKey={source.includeOrg ? strings.yes : strings.no}    
        options={yesNoOptions}
        label={strings.HROnboarding.includeOrg}
        onChange={handleIncludeOrgChange}
        styles={{
          root: classNames.horizontalChoiceGroup,
          flexContainer: classNames.horizontalChoiceGroupContainer
        }}
      />

      {(source.includeOrg === true) && (
      <Stack horizontal horizontalAlign="space-between" verticalAlign="center" tokens={stackTokens}>
        <Stack.Item align="start">
          <div>
             <div className={classNames.labelContainer}>
              <Label>{strings.HROnboarding.orgLeaderId}</Label>
              <TooltipHost content={strings.HROnboarding.orgLeaderInfo} id="toolTipOrgLeaderId" calloutProps={{ gapSpace: 0 }}>
                <IconButton iconProps={{ iconName: "Info" }} aria-describedby="toolTipOrgLeaderId" />
              </TooltipHost>
            </div>
            <TextField
              placeholder={strings.HROnboarding.orgLeaderIdPlaceHolder}
              value={source.ids && source.ids.length > 0 ? source.ids.toString() : ''}
              onChange={handleOrgLeaderIdChange}
              styles={{fieldGroup: classNames.textFieldFieldGroup}}
              validateOnLoad={false}
              validateOnFocusOut={false}
            ></TextField>
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
              value={source.depth?.toString()}
              min={0}
              max={100}
              step={1}
              onChange={handleDepthChange}
              incrementButtonAriaLabel={strings.HROnboarding.incrementButtonAriaLabel}
              decrementButtonAriaLabel={strings.HROnboarding.decrementButtonAriaLabel}
              styles={{root: classNames.spinButton}}
            />
          </div>
        </Stack.Item>

        <Stack.Item align="start">
          <div>
            <ChoiceGroup
              selectedKey={(source.filter?.includes(excludeLeaderQuery))? strings.no : strings.yes}
              options={yesNoOptions}
              label={strings.HROnboarding.includeLeader}
              onChange={handleIncludeLeaderChange}
              styles={{
                root: classNames.horizontalChoiceGroup,
                flexContainer: classNames.horizontalChoiceGroupContainer
              }}
            />
          </div>
        </Stack.Item>
      </Stack>
      )}

      <ChoiceGroup
        selectedKey={source.includeFilter ? strings.yes : strings.no}  
        options={yesNoOptions}
        label={strings.HROnboarding.includeFilter}
        onChange={handleIncludeFilterChange}
        styles={{
          root: classNames.horizontalChoiceGroup,
          flexContainer: classNames.horizontalChoiceGroupContainer
        }}
      />

      {(source.includeFilter === true) && (
      <><div className={classNames.labelContainer}>
      <Label>{strings.HROnboarding.filter}</Label>
      <TooltipHost content={strings.HROnboarding.filterInfo} id="toolTipFilterId" calloutProps={{ gapSpace: 0 }}>
        <IconButton iconProps={{ iconName: "Info" }} aria-describedby="toolTipFilterId" />
      </TooltipHost>
      </div>
      <TextField
        placeholder={strings.HROnboarding.filterPlaceHolder}
        multiline rows={3}
        resizable={true}
        value={source.filter?.toString()}
        onChange={handleFilterChange}
        styles={{ fieldGroup: classNames.filter }}
        validateOnLoad={false}
        validateOnFocusOut={false}
      ></TextField></>
      )}

      <div className={classNames.error}>
        {errorMessage}
      </div>

    </div>
  );
};