// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Job } from "../models/Job";
import { createAsyncThunk } from "@reduxjs/toolkit";
import { loginRequest, config } from "../authConfig";
import { msalInstance } from "../index";
import moment from "moment";
import { SyncStatus, ActionRequired } from "../models/Status";

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
      var hoursLeft = Math.abs(currentTime.diff(nextRunTime, "hours"));
      index['estimatedNextRunTime'] = moment.utc(index["estimatedNextRunTime"]).local().format("MM/DD/YYYY") + " " + hoursLeft.toString() + " hrs left";

      index["enabledOrNot"] = index["status"] === "CustomerPaused" ? "Disabled" : "Enabled"

      return index;
    });

    const newPayload = mapped.map((index) => {
      switch (index["status"]) {
        case SyncStatus.ThresholdExceeded:
          index["actionRequired"] = ActionRequired.ThresholdExceeded;
          break;
        case SyncStatus.CustomerPaused:
          index["actionRequired"] = ActionRequired.CustomerPaused;
          break;
        case SyncStatus.CustomMembershipDataNotFound:
          index["actionRequired"] = ActionRequired.CustomMembershipDataNotFound;
          break;
        case SyncStatus.DestinationGroupNotFound:
          index["actionRequired"] = ActionRequired.DestinationGroupNotFound;
          break;
        case SyncStatus.NotOwnerOfDestinationGroup:
          index["actionRequired"] = ActionRequired.NotOwnerOfDestinationGroup;
          break;
        case SyncStatus.SecurityGroupNotFound:
          index["actionRequired"] = ActionRequired.SecurityGroupNotFound;
          break;
      }
      return index;
    });

    return newPayload;

  } catch (error) {
    console.log(error);
  }
});
