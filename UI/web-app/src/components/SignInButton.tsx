// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useMsal } from '@azure/msal-react';
import { DefaultButton } from '@fluentui/react/lib/Button';
import { useTranslation } from 'react-i18next';

export const SignInButton = () => {
    const { instance } = useMsal();
    const { t } = useTranslation();

    const handleSignIn = () => {
        instance.loginRedirect({
            scopes: ["User.Read"]
        });
    }
    
    return (
        <div>
            <DefaultButton onClick={handleSignIn}>{t('signIn')}</DefaultButton>
        </div>
    )
};
