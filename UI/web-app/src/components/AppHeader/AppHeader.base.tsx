// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.import React from "react";

import { classNamesFunction, IProcessedStyleSet } from "@fluentui/react";
import { useTheme } from "@fluentui/react/lib/Theme";
import WelcomeName from "../WelcomeName";
import SignInSignOutButton from "../SignInSignOutButton";
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
      <div className={classNames.root} role="banner" aria-label="header">
        <WelcomeName></WelcomeName>
        <SignInSignOutButton/>
      </div>
    </header>
  );
};
