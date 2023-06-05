// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { InteractionStatus } from '@azure/msal-browser'
import { useIsAuthenticated, useMsal } from '@azure/msal-react'

import { SignInButton } from './SignInButton'
import { SignOutButton } from './SignOutButton'

const SignInSignOutButton = () => {
  const { inProgress } = useMsal()
  const isAuthenticated = useIsAuthenticated()

  if (isAuthenticated) {
    return (
        <div>
            <SignOutButton />
        </div>
    )
  } else if (inProgress !== InteractionStatus.Startup && inProgress !== InteractionStatus.HandleRedirect) {
    return <SignInButton />
  } else {
    return null
  }
}

export default SignInSignOutButton
