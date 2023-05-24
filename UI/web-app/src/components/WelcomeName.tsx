// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useSelector } from "react-redux";
import { selectAccountName } from '../store/account.slice'


const WelcomeName = () => {

    const name: string | undefined = useSelector(selectAccountName);

    if (name) {
        return <div>Welcome, {name}</div>;
    } else {
        return null;
    }
};

export default WelcomeName;