// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import WelcomeName from "./WelcomeName";
import SignInSignOutButton from "./SignInSignOutButton";

const NavBar = () => {
    return (
        <div>
            <WelcomeName />
            <SignInSignOutButton />
        </div>
    );
};

export default NavBar;