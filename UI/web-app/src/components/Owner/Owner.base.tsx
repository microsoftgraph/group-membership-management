// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useState } from 'react';
import { useSelector, useDispatch } from 'react-redux';

import {
  type IOwnerProps,
  type IOwnerStyleProps,
  type IOwnerStyles,
} from './Owner.types';
import { type AppDispatch } from '../../store';
import { addOwner } from '../../store/owner.api';
import { selectOwner } from '../../store/owner.slice';

import {
  classNamesFunction,
  DefaultButton,
  Text,
  type IProcessedStyleSet,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { TextField } from '@fluentui/react/lib/TextField';
import { useStrings } from '../../store/hooks';

const getClassNames = classNamesFunction<IOwnerStyleProps, IOwnerStyles>();

export const OwnerBase: React.FunctionComponent<IOwnerProps> = (
  props: IOwnerProps
) => {
  const { className, styles } = props;
  const [textFieldValue, setTextFieldValue] = useState('');

  const classNames: IProcessedStyleSet<IOwnerStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  const strings = useStrings();
  const groupIdPlaceHolder = strings.groupIdPlaceHolder;
  const dispatch = useDispatch<AppDispatch>();
  const owner = useSelector(selectOwner);

  const onClick = () => {
    dispatch(addOwner(textFieldValue));
  };

  const onChangeTextFieldValue = (
    event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>,
    newValue?: string
  ) => {
    setTextFieldValue(newValue || '');
  };

  return (
    <div className={classNames.root}>
      <Text variant="large">{strings.groupIdHeader}</Text>
      <p />
      <TextField
        ariaLabel={groupIdPlaceHolder}
        placeholder={groupIdPlaceHolder}
        onChange={onChangeTextFieldValue}
        tabIndex={0}
      />
      <p />
      <DefaultButton onClick={onClick}>{strings.okButton}</DefaultButton>
      <p />
      {(() => {
        if (owner.status === '') {
          return <div />;
        } else if (owner.status === 'false 403 Forbidden') {
          return <div>{strings.addOwner403Message}</div>;
        } else if (owner.status === 'false 400 Bad Request') {
          return <div>{strings.addOwner400Message}</div>;
        } else if (owner.status === 'true 204 No Content') {
          return <div>{strings.addOwner204Message}</div>;
        } else {
          return <div>{strings.addOwnerErrorMessage}</div>;
        }
      })()}
    </div>
  );
};
