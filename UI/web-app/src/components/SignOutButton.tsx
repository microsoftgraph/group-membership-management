// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useMsal } from '@azure/msal-react';
import { DefaultButton } from '@fluentui/react/lib/Button';

export const SignOutButton = () => {
  const { instance } = useMsal();
  const handleSignOut = () => {
    instance.logoutRedirect();
  };
  return (
    <div>
      <DefaultButton onClick={handleSignOut}>Sign out</DefaultButton>
    </div>
  );
};
