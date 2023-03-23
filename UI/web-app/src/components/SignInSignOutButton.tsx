// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useIsAuthenticated, useMsal } from "@azure/msal-react";
import { SignInButton } from "./SignInButton";
import { SignOutButton } from "./SignOutButton";
import { InteractionStatus } from "@azure/msal-browser";
import { Job } from './Job';
import Loader from './Loader';
import { useState, useEffect } from 'react';
import { callMsGraph } from "../utils/MsGraphApiCall";

function GetJobs() {
    const [jobs, setJobs] = useState([]);
    const [err, setError] = useState({});

    useEffect(() => {
        callMsGraph()
        .then(response => setJobs(response))
        .catch(err => setError(err));
    }, [])

    return (
        <div>
          {jobs.length > 0 ? <Job jobs={jobs} /> : (<Loader />)}
        </div>

    );
  }

const SignInSignOutButton = () => {
    const { inProgress } = useMsal();
    const isAuthenticated = useIsAuthenticated();

    if (isAuthenticated) {

        return (
        <div>
            <SignOutButton />
            <GetJobs />

        </div>);
    } else if (inProgress !== InteractionStatus.Startup && inProgress !== InteractionStatus.HandleRedirect) {
        return <SignInButton />;
    } else {
        return null;
    }
}

export default SignInSignOutButton;