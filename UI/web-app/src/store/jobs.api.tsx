// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Job } from "../models/Job";
import { createAsyncThunk } from "@reduxjs/toolkit";
import { loginRequest, config } from "../authConfig";
import { msalInstance } from "../index";
import moment from "moment";

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

    const mapped = payload.map((index)=> {

      const currentTime = moment.utc();
      var lastRunTime = moment(index['lastSuccessfulRunTime']);
      var hoursAgo = currentTime.diff(lastRunTime, "hours");
      index['lastSuccessfulRunTime'] = moment.utc(index["lastSuccessfulRunTime"]).local().format("MM/DD/YYYY") + " " + hoursAgo.toString() + " hrs ago";

      var nextRunTime = moment(index['estimatedNextRunTime']);
      var hoursLeft = currentTime.diff(nextRunTime, "hours");
      index['estimatedNextRunTime'] = moment.utc(index["estimatedNextRunTime"]).local().format("MM/DD/YYYY") + " " + hoursLeft.toString() + " hrs left";

      index["enabledOrNot"] = index["status"] === "CustomerPaused" ? "Disabled" : "Enabled"

      return index;
    });

    const newPayload = mapped.map((index)=> {
     if (index["status"] === "ThresholdExceeded") {
      index["actionRequired"] = "Threshold Exceeded"
     } else if (index["status"] === "CustomerPaused") {
      index["actionRequired"] = "Customer Paused"
     } else if (index["status"] === "CustomMembershipDataNotFound") {
      index["actionRequired"] = "No users in the source"
     } else if (index["status"] === "DestinationGroupNotFound") {
      index["actionRequired"] = "Destination Group Not Found"
     } else if (index["status"] === "NotOwnerOfDestinationGroup") {
      index["actionRequired"] = "Not Owner Of Destination Group"
     } else if (index["status"] === "SecurityGroupNotFound") {
      index["actionRequired"] = "Security Group Not Found"
     }
      return index;
    });

    return newPayload;

  } catch (error) {
    console.log(error);
  }
});
