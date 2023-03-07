// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
import { useTranslation } from 'react-i18next';

const Loader = () => {

    const { t } = useTranslation();

    return (<>
        {t('emptyList')}
    </>)
}

export default Loader