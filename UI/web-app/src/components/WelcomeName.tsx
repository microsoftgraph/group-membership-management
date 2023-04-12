// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useSelector } from "react-redux";
import { selectAccountName } from '../store/account.slice'


const WelcomeName = () => {

    const name: string | undefined = useSelector(selectAccountName);
    const firstName: string | undefined = name ? name.split(" ")[0] : undefined;

    if (firstName) {
        return <div>Welcome, {firstName}</div>;
    } else {
        return null;
    }
};

export default WelcomeName;