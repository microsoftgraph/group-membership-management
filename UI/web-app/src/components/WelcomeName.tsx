// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useSelector } from "react-redux";
import { selectAccountName } from '../store/account.slice'
import { useTranslation } from 'react-i18next';


const WelcomeName = () => { 
    const { t } = useTranslation();

    const name: string | undefined = useSelector(selectAccountName);

    if (name) {
        return <div>{t('welcome')}, {name}.</div>;
    } else {
        return null;
    }
};

export default WelcomeName;
