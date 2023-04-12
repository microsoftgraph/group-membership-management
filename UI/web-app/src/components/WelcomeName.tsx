// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useSelector } from "react-redux";
import { selectAccountName } from '../store/account.slice'


const WelcomeName = () => {

    const name = useSelector(selectAccountName);

    if (name) {
        return <div>Welcome, {name.split(" ")[0]}</div>;
    } else {
        return null;
    }
};

export default WelcomeName;