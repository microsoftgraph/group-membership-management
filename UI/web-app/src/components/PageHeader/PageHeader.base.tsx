// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from "react";
import { classNamesFunction, IProcessedStyleSet } from "@fluentui/react";
import { useTheme } from "@fluentui/react/lib/Theme";
import { useNavigate } from "react-router-dom";
import { IIconProps } from "@fluentui/react";
import { CommandBarButton } from "@fluentui/react/lib/Button";
import { Stack, IStackStyles } from "@fluentui/react/lib/Stack";
import { Separator } from "@fluentui/react";
import {
  IPageHeaderProps,
  IPageHeaderStyleProps,
  IPageHeaderStyles,
} from "./PageHeader.types";

const getClassNames = classNamesFunction<
  IPageHeaderStyleProps,
  IPageHeaderStyles
>();

const stackStyles: Partial<IStackStyles> = { root: { height: 44 } };
const addIcon: IIconProps = { iconName: "ChevronLeftMed" };

export const PageHeaderBase: React.FunctionComponent<IPageHeaderProps> = (
  props: IPageHeaderProps
) => {
  const { className, styles } = props;
  const classNames: IProcessedStyleSet<IPageHeaderStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  const navigate = useNavigate();

  const backButtonOnClick = (): void => {
    navigate(-1);
  };

  return (
    <Stack className={classNames.root}>
      <Stack horizontal styles={stackStyles}>
        <CommandBarButton
          iconProps={addIcon}
          text="Back"
          onClick={backButtonOnClick}
        />
      </Stack>
      <Separator></Separator>
    </Stack>
  );
};
