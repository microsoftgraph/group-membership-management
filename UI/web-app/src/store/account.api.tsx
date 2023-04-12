// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Account } from "../models/Account";
import { createAsyncThunk } from "@reduxjs/toolkit";
import { IMsalContext } from "@azure/msal-react";
import { msalInstance } from "../index";
import { AccountInfo } from "@azure/msal-browser";

export const fetchAccount = createAsyncThunk(
  "account/fetchAccount",
  async (context: IMsalContext) => {
    var account: AccountInfo | null = msalInstance.getActiveAccount();

    if (!account) {
      try {
        context.instance.loginRedirect({
          scopes: ["User.Read"],
        });
      } catch (error) {
        console.log(error);
      }

      const account = context.accounts[0];

      if (account) {
        const payload: Account = {
          ...account,
        };

        return payload;
      }
    } else {
      
      const payload: Account = {
        ...account,
      };

      return payload;
    }
  }
);
