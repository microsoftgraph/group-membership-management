// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useSelector } from "react-redux";
import { selectAccountName } from '../store/account.slice'


const WelcomeName = () => {

    const name: string = useSelector(selectAccountName);
    const firstName: string = name.split(" ")[0]

    if (firstName) {
        return <div>Welcome, {firstName}</div>;
    } else {
        return null;
    }
};

export default WelcomeName;