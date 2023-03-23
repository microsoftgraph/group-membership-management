// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { loginRequest, config, msalConfig } from "../authConfig";
import { msalInstance } from "../index";

export async function callMsGraph() {
    const account = msalInstance.getActiveAccount();
    if (!account) {
        throw Error("No active account! Verify a user has been signed in and setActiveAccount has been called.");
    }

    const response = await msalInstance.acquireTokenSilent({
        ...loginRequest,
        account: account
    });

    const headers = new Headers();
    const bearer = `Bearer ${response.accessToken}`;
    console.log(response.accessToken);
    console.log(msalConfig.auth.clientId);
    console.log(msalConfig.auth.authority);
    console.log(loginRequest.scopes);
    console.log(config.endpoint);


    headers.append("Authorization", bearer);

    const options = {
        method: "GET",
        headers: headers
    };

    return fetch(config.endpoint, options)
        .then(response => response.json())
        .catch(error => console.log(error));
}