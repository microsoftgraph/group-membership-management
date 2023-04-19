// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useTranslation } from 'react-i18next';
import '../i18n/config';
import { DefaultButton } from '@fluentui/react/lib/Button';
import { useNavigate } from 'react-router-dom';

const AddOwner = () => {
        const { t } = useTranslation();
        const navigate = useNavigate();
        const onClick = (item?: any, index?: number, ev?: React.FocusEvent<HTMLElement>): void => {
                navigate('/OwnerPage', { replace: false, state: {item: 1} })
        }

        return (
        <div>
                <DefaultButton onClick={onClick}>{t('addOwnerButton')}</DefaultButton>
        </div>
        );
}

export default AddOwner;