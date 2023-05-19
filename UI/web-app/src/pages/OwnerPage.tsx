// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from "react";
import { Page } from "../components/Page";
import { PageHeader } from "../components/PageHeader";
import { Owner } from '../components/Owner/Owner';

export const OwnerPage: React.FunctionComponent = () => {

  return (
    <Page>
      <PageHeader/>
      <Owner/>
    </Page>
  );
};