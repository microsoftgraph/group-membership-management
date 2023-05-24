// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useMsal } from '@azure/msal-react';
import { DefaultButton } from '@fluentui/react/lib/Button';
import { useTranslation } from 'react-i18next';

export const SignOutButton = () => {
    const { t } = useTranslation();
    const { instance } = useMsal();
    const handleSignOut = () => {
        instance.logoutRedirect();
    }
    return (
        <div>
        <DefaultButton onClick={handleSignOut}>{t('signOut')}</DefaultButton>
    </div>
  );
};
