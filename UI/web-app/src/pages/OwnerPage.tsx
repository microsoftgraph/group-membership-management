// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from "react";
import { useState } from 'react';
import { Page } from "../components/Page";
import { DefaultButton, Text } from "@fluentui/react";
import { PageHeader } from "../components/PageHeader";
import { TextField } from '@fluentui/react/lib/TextField';
import { callMsGraph } from "../utils/MsGraphApiCall";

export const OwnerPage: React.FunctionComponent = () => {

  const [TextFieldValue, setTextFieldValue] = React.useState('');
  const [owner, setOwner] = useState('');
  const [err, setError] = useState({});

  const onClick = () => {
    callMsGraph(TextFieldValue)
      .then(response => setOwner(response))
      .catch(err => setError(err));
  }

  const onChangeTextFieldValue = React.useCallback(
    (event: React.FormEvent<HTMLInputElement | HTMLTextAreaElement>, newValue?: string) => {
      setTextFieldValue(newValue || '');
    }, [],
  );

  return (
    <Page>
      <PageHeader/>
      <Text variant="xxLarge">Group Details</Text>
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
        if (owner == "false 403 Forbidden") {
          return (
            <div>Insufficient privileges to complete the operation</div>
          )
        } else if (owner == "false 400 Bad Request") {
          return (
            <div>GMM is already added as an owner</div>
          )
        } else if (owner == "true 204 No Content") {
          return (
            <div>Successfully added</div>
          )
        }
      })()}

    </Page>
  );
};