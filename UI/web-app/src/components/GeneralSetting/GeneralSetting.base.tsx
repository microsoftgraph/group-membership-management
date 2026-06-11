import React, { useState } from 'react';
import { classNamesFunction, Toggle } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import type { GeneralSettingProps, GeneralSettingStyles, GeneralSettingStyleProps } from './GeneralSetting.types';

export const getClassNames = classNamesFunction<GeneralSettingStyleProps, GeneralSettingStyles>();

export const GeneralSettingBase: React.FunctionComponent<GeneralSettingProps> = (props: GeneralSettingProps) => {
  const { title, description, className, generalSettingValue, onGeneralSettingChange, styles, id} = props;
  const classNames = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  const isGeneralSettingEnabled = generalSettingValue === 'true';
  const [isToggleEnabled, setIsToggleEnabled] = useState<boolean>(isGeneralSettingEnabled);

  const handleSubmissionReviewerSettingChange = (ev: React.MouseEvent<HTMLElement>, checked?: boolean) => {
    const wrappedValue = checked ? "true" : "false";
    onGeneralSettingChange(wrappedValue);
    setIsToggleEnabled(checked ?? false);
  };

  return (
    <div className={classNames.card}>
      <div className={classNames.title}>{title}</div>
      <div className={classNames.description}>{description}</div>
      <Toggle
          id={id}
          title={title}
          inlineLabel={true}
          checked={isToggleEnabled}
          onChange={handleSubmissionReviewerSettingChange}
        />
    </div>
  );
};
