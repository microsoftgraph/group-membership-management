import React from 'react';
import { classNamesFunction, IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { IPageProps, IPageStyleProps, IPageStyles } from './Page.types';

const getClassNames = classNamesFunction<IPageStyleProps, IPageStyles>();

export const PageBase: React.FunctionComponent<IPageProps> = (props: IPageProps) => {
  const { children, className, styles } = props;
  const classNames: IProcessedStyleSet<IPageStyles> = getClassNames(styles, {
    className,
    theme: useTheme()
  });

  return (
    <div className={classNames.root}>
      {children}
    </div>
  );
};