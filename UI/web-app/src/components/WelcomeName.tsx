// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useSelector } from "react-redux";
import { selectAccountName } from '../store/account.slice'
import { useTranslation } from 'react-i18next';


const WelcomeName = () => { 
    const { t, i18n } = useTranslation();
    const currentDate = new Date();
    const formattedDate = currentDate.toLocaleDateString(i18n.language);
    const formattedTime = currentDate.toLocaleTimeString(i18n.language);

    const name: string | undefined = useSelector(selectAccountName);

    if (name) {
        return <div>{t('welcome')}, {name}. {formattedDate}, {formattedTime}</div>;
    } else {
        return null;
    }
};

export default WelcomeName;
