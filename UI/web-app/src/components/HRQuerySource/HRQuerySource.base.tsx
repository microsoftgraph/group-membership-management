// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import { classNamesFunction, Stack, type IProcessedStyleSet, IStackTokens } from '@fluentui/react';
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
    childrenGap: 20
  };

  const [source, setSource] = useState<HRSourcePartSource>(props.source);

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

  return (
    <div>
      <Stack horizontal horizontalAlign="space-between" verticalAlign='center' tokens={stackTokens}>
        <TextField
          label={strings.HROnboarding.orgLeaderId}
          placeholder={strings.HROnboarding.orgLeaderIdPlaceHolder}
          value={source.ids && source.ids.length > 0 ? source.ids.join(',') : ''}
          onChange={handleOrgLeaderIdChange}
          styles={{fieldGroup: classNames.textFieldFieldGroup}}
          validateOnLoad={false}
          validateOnFocusOut={false}
          required
        ></TextField>

        <TextField
          label={strings.HROnboarding.depth}
          placeholder={strings.HROnboarding.depthPlaceHolder}
          value={source.depth?.toString()}
          onChange={handleDepthChange}
          styles={{fieldGroup: classNames.textFieldFieldGroup}}
          validateOnLoad={false}
          validateOnFocusOut={false}
        ></TextField>

        <TextField
          label={strings.HROnboarding.filter}
          placeholder={strings.HROnboarding.filterPlaceHolder}
          value={source.filter?.toString()}
          onChange={handleFilterChange}
          styles={{fieldGroup: classNames.textFieldFieldGroup}}
          validateOnLoad={false}
          validateOnFocusOut={false}
        ></TextField>
      </Stack>
    </div>
  );
};