// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { graphRequest } from "../authConfig";
import { msalInstance } from "../index";

export async function callMsGraph(groupId: string) {
    const account = msalInstance.getActiveAccount();

    if (!account) {
        throw Error("No active account! Verify a user has been signed in and setActiveAccount has been called.");
    }

    const response = await msalInstance.acquireTokenSilent({
        ...graphRequest,
        account: account
    });

    const headers = new Headers();
    const bearer = `Bearer ${response.accessToken}`;
    headers.append("Authorization", bearer);
    headers.append("Scopes", "Group.ReadWrite.All");
    headers.append("Content-Type", "application/json");

    const options = {
        method: "POST",
        headers: headers,
        body: JSON.stringify({"@odata.id": "https://graph.microsoft.com/v1.0/serviceprincipals/913de83c-ec21-484d-aa74-84e364171851"})
    };

    var url = `https://graph.microsoft.com/v1.0/groups/${groupId}/owners/$ref/`;
    var resp: Response;
    return fetch(url, options)
    .then(response => {
        resp = response;
        console.log(resp.ok + " " +resp.status.toString() + " " +resp.statusText)
        return (resp.ok + " " +resp.status.toString() + " " +resp.statusText);
      });
    }