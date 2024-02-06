// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { classNamesFunction, Stack, type IProcessedStyleSet, IStackTokens, Label, IconButton, TooltipHost, ChoiceGroup, IChoiceGroupOption } from '@fluentui/react';
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
    const ids = newValue.trim() !== '' ? newValue.split(',').map(str => Number(str.trim())) : [];
    setSource(prevSource => {
        const newSource = { ...prevSource, ids };
        onSourceChange(newSource, partId);
        return newSource;
    });
  };

  const handleDepthChange = (_: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '') => {
    const depth = newValue !== '' ? parseInt(newValue, 10) : undefined;
    setSource(prevSource => {
        const newSource = { ...prevSource, depth };
        onSourceChange(newSource, partId);
        return newSource;
    });
  };

  const handleFilterChange = (_: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '') => {
    const filter = newValue;
    setSource(prevSource => {
        const newSource = { ...prevSource, filter };
        onSourceChange(newSource, partId);
        return newSource;
    });
  };

  const includeLeaderOptions: IChoiceGroupOption[] = [
    { key: 'Yes', text: strings.yes },
    { key: 'No', text: strings.no }
  ];

  const handleIncludeLeaderChange = (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, option?: IChoiceGroupOption) => {
    setErrorMessage('');
    let filter: string;
    if (option?.key == "No") {
      if (!source.ids || source.ids.length === 0)
      {
        setErrorMessage(strings.HROnboarding.errorMessage);
        return;
      }
      if (source.filter !== "") {
        filter = `${source.filter} and ` + excludeLeaderQuery;
      } else {
        filter = excludeLeaderQuery;
      }
    }
    else if (option?.key == "Yes") {
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
      <Stack horizontal horizontalAlign="space-between" verticalAlign="center" tokens={stackTokens}>
        <Stack.Item align="start">
          <div>
             <div className={classNames.labelContainer}>
              <Label>{strings.HROnboarding.orgLeaderId}</Label>
              <TooltipHost content={strings.HROnboarding.orgLeaderInfo} id="toolTipId" calloutProps={{ gapSpace: 0 }}>
                <IconButton iconProps={{ iconName: "Info" }} aria-describedby="toolTipId" />
              </TooltipHost>
            </div>
            <TextField
              placeholder={strings.HROnboarding.orgLeaderIdPlaceHolder}
              value={source.ids && source.ids.length > 0 ? source.ids.join(',') : ''}
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
              <TooltipHost content={strings.HROnboarding.depthInfo} id="toolTipId" calloutProps={{ gapSpace: 0 }}>
                <IconButton iconProps={{ iconName: "Info" }} aria-describedby="toolTipId" />
              </TooltipHost>
            </div>
            <TextField
              placeholder={strings.HROnboarding.depthPlaceHolder}
              value={source.depth?.toString()}
              onChange={handleDepthChange}
              styles={{fieldGroup: classNames.textFieldFieldGroup}}
              validateOnLoad={false}
              validateOnFocusOut={false}
            ></TextField>
          </div>
        </Stack.Item>

        <Stack.Item align="start">
          <div>
             <div className={classNames.labelContainer}>
              <Label>{strings.HROnboarding.filter}</Label>
              <TooltipHost content={strings.HROnboarding.filterInfo} id="toolTipId" calloutProps={{ gapSpace: 0 }}>
                <IconButton iconProps={{ iconName: "Info" }} aria-describedby="toolTipId" />
              </TooltipHost>
            </div>
            <TextField
              placeholder={strings.HROnboarding.filterPlaceHolder}
              value={source.filter?.toString()}
              onChange={handleFilterChange}
              styles={{fieldGroup: classNames.textFieldFieldGroup}}
              validateOnLoad={false}
              validateOnFocusOut={false}
            ></TextField>
          </div>
        </Stack.Item>

        <Stack.Item align="start">
          <div>
            <ChoiceGroup
              selectedKey={(source.filter?.includes(excludeLeaderQuery))? strings.no : strings.yes}
              options={includeLeaderOptions}
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

      <div className={classNames.error}>
        {errorMessage}
      </div>

    </div>
  );
};