// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { DefaultButton } from '@fluentui/react/lib/Button';
import { useNavigate } from 'react-router-dom';

const AddOwner = () => {
        const navigate = useNavigate();
        const onClick = (item?: any, index?: number, ev?: React.FocusEvent<HTMLElement>): void => {
                navigate('/OwnerPage', { replace: false, state: {item: 1} })
        }

        return (
        <div>
                <DefaultButton onClick={onClick}>Add GMM as an owner</DefaultButton>
        </div>
        );
}

export default AddOwner;