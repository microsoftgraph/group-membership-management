import { Job } from "../models/Job";
import { createAsyncThunk } from "@reduxjs/toolkit";

import { loginRequest, config } from "../authConfig";
import { msalInstance } from "../index";

export const fetchJobs = createAsyncThunk("jobs/fetchJobs", async () => {

  const account = msalInstance.getActiveAccount();
  if (!account) {
    throw Error(
      "No active account! Verify a user has been signed in and setActiveAccount has been called."
    );
  }

  const authResult = await msalInstance.acquireTokenSilent({
    ...loginRequest,
    account: account,
  });

  const headers = new Headers();
  const bearer = `Bearer ${authResult.accessToken}`;
  headers.append("Authorization", bearer);

  const options = {
    method: "GET",
    headers: headers,
  };

  try {
    let response = await fetch(config.endpoint, options).then((response) =>
      response.json()
    );

    const payload: Job[] = response;

    return payload;

  } catch (error) {
    console.log(error);
  }
});
