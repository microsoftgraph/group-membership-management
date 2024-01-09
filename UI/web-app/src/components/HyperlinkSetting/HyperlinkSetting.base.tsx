// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { TextField } from '@fluentui/react/lib/TextField';
import type {
  HyperlinkSettingProps,
  HyperlinkSettingStyleProps,
  HyperlinkSettingStyles,
} from './HyperlinkSetting.types';
import { useStrings } from '../../store/hooks';

export const getClassNames = classNamesFunction<HyperlinkSettingStyleProps, HyperlinkSettingStyles>();

export const HyperlinkSettingBase: React.FunctionComponent<HyperlinkSettingProps> = (props: HyperlinkSettingProps) => {
  const { title, description, link, onLinkChange, onValidation, className, styles } = props;
  const classNames: IProcessedStyleSet<HyperlinkSettingStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  const handleChange = (_: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue: string = '') => {
    onLinkChange(newValue);
  };

  const handleGetErrorMessage = (value: string): string => {
    if (value.trim() === '') {
      onValidation(true);
      return '';
    }
    
    let isValid = false;
    try {
        // because Url.canParse doesn't exist
      const _ = new URL(value);
      isValid = true;
    } catch (e) {
      isValid = false;
    }

    onValidation(isValid);
    return isValid ? '' : strings.AdminConfig.hyperlinkContainer.invalidUrl;
  };

  return (
    <div className={classNames.card}>
      <div className={classNames.title}>{title}</div>
      <div className={classNames.description}>{description}</div>
      <div>
        <TextField
          label={strings.AdminConfig.hyperlinkContainer.address}
          placeholder={strings.AdminConfig.hyperlinkContainer.addHyperlink}
          value={link}
          onChange={handleChange}
          styles={{
            fieldGroup: classNames.textFieldFieldGroup,
          }}
          onGetErrorMessage={handleGetErrorMessage}
          validateOnLoad={false}
          validateOnFocusOut={true}
        ></TextField>
      </div>
    </div>
  );
};
