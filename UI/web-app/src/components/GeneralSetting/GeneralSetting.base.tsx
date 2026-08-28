import React from 'react';
import { classNamesFunction, Toggle } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import type { GeneralSettingProps, GeneralSettingStyles, GeneralSettingStyleProps } from './GeneralSetting.types';

export const getClassNames = classNamesFunction<GeneralSettingStyleProps, GeneralSettingStyles>();

export const GeneralSettingBase: React.FunctionComponent<GeneralSettingProps> = (props: GeneralSettingProps) => {
  const { title, description, className, generalSettingValue, onGeneralSettingChange, styles, id, disabled } = props;
  const classNames = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  // Controlled by the parent so the toggle stays in sync when settings load or reset.
  const isToggleEnabled = generalSettingValue === 'true';

  const handleSubmissionReviewerSettingChange = (ev: React.MouseEvent<HTMLElement>, checked?: boolean) => {
    onGeneralSettingChange(checked ? "true" : "false");
  };

  return (
    <div className={classNames.card}>
      <div className={classNames.titleRow}>
        <div className={classNames.title}>{title}</div>
        <Toggle
          id={id}
          title={title}
          checked={isToggleEnabled}
          disabled={disabled}
          onChange={handleSubmissionReviewerSettingChange}
          styles={{ root: { marginBottom: 0 } }}
        />
      </div>
      <div className={classNames.description}>{description}</div>
    </div>
  );
};
