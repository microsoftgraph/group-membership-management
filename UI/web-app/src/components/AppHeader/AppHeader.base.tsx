// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.import React from "react";

import { classNamesFunction, IProcessedStyleSet } from "@fluentui/react";
import { useTheme } from "@fluentui/react/lib/Theme";
import WelcomeName from "../WelcomeName";
import { SettingsIcon } from "@fluentui/react-icons-mdl2";
import {
  IAppHeaderProps,
  IAppHeaderStyleProps,
  IAppHeaderStyles,
} from "./AppHeader.types";

const getClassNames = classNamesFunction<
  IAppHeaderStyleProps,
  IAppHeaderStyles
>();

export const AppHeaderBase: React.FunctionComponent<IAppHeaderProps> = (
  props: IAppHeaderProps
) => {
  const { className, styles } = props;
  const classNames: IProcessedStyleSet<IAppHeaderStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  return (
    <header>
      <div className={classNames.whole}>
      <div className={classNames.root} role="banner" aria-label="header">
        <div className={classNames.left}> Membership Management </div>
        <div className={classNames.right}><SettingsIcon /></div>
      </div>
      <br />
      <div className={classNames.welcome} role="banner" aria-label="header">
        <WelcomeName />
      </div>
      <div className={classNames.learn} role="banner" aria-label="header">
        <br/> Learn how Membership Management works in your organization <br />
      </div>
      < br/>
      </div>
    </header>
  );
};
