// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useMsal } from "@azure/msal-react";
import { DefaultButton } from '@fluentui/react/lib/Button';
import { useEffect } from "react";

export const SignInButton = () => {
    const { instance } = useMsal();

    const handleSignIn = () => {
        instance.loginRedirect({
            scopes: ["User.Read"]
        });
    }
    
    return (
        <div>
            <DefaultButton onClick={handleSignIn}>Sign in</DefaultButton>
        </div>
    )
};