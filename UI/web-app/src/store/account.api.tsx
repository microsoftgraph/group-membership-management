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
        console.log("fetchAccount: No account found, logging user in.");
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
      console.log(
        "fetchAccount: Account found in state. Returning account from state."
      );

      const payload: Account = {
        ...account,
      };

      console.log("fetchAccount: This is the account found:");
      console.dir(payload);

      return payload;
    }
  }
);
