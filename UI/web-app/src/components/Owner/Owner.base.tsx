// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useTranslation } from 'react-i18next';
import { useState } from 'react'
import { useSelector, useDispatch } from 'react-redux'
import { addOwner } from '../../store/owner.api'
import { selectOwner } from '../../store/owner.slice'
import { AppDispatch } from "../../store";
import { classNamesFunction, DefaultButton, Text,IProcessedStyleSet } from "@fluentui/react";
import { useTheme } from "@fluentui/react/lib/Theme";
import { TextField } from '@fluentui/react/lib/TextField';
import {
  IOwnerProps,
  IOwnerStyleProps,
  IOwnerStyles,
} from "./Owner.types";

const getClassNames = classNamesFunction<
  IOwnerStyleProps,
  IOwnerStyles
>();

export const OwnerBase: React.FunctionComponent<IOwnerProps> = (
  props: IOwnerProps
) => {
  const { className, styles } = props;
  const [textFieldValue, setTextFieldValue] = useState('');

  const classNames: IProcessedStyleSet<IOwnerStyles> = getClassNames(
    styles,
    {
      className,
      theme: useTheme(),
    }
  );

  const { t } = useTranslation();
  const dispatch = useDispatch<AppDispatch>()
  const owner = useSelector(selectOwner)

  const onClick = () => {
    dispatch(addOwner(textFieldValue))
  }

  const onChangeTextFieldValue = (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string) => {
    setTextFieldValue(newValue || '');
  }

  return (
    <div className={classNames.root}>
      <Text variant="xLarge">New Group Details</Text>
      <p />
      <TextField
          ariaLabel={"GroupId"}
          placeholder={"Group Id"}
          onChange={onChangeTextFieldValue}
          tabIndex={0}
        />
      <p />
      <DefaultButton onClick={onClick}>OK</DefaultButton>
      <p />
      {(() => {
        if (owner.status === "") {
          return (
            <div />
          )
        }
        else if (owner.status === "false 403 Forbidden") {
          return (
            <div>You do not have permission to complete this operation.</div>
          )
        } else if (owner.status === "false 400 Bad Request") {
          return (
            <div>GMM is already added as an owner.</div>
          )
        } else if (owner.status === "true 204 No Content") {
          return (
            <div>Added Successfully.</div>
          )
        } else {
          return (
            <div>We are having trouble adding GMM as the owner. Please try again later.</div>
          )
        }
      })()}
    </div>
  );
}