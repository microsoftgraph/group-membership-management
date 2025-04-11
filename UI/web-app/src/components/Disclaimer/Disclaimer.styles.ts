import { IStyleFunctionOrObject, IStackStyles } from '@fluentui/react';
import { IDisclaimerStyleProps, IDisclaimerStyles } from './Disclaimer.types';

export const getStyles: IStyleFunctionOrObject<IDisclaimerStyleProps, IDisclaimerStyles> = () => {
    return {
        root: {
        },
        modalContainer: {
            padding: 40,
        },
        buttonContainer: {
            display: 'flex',
            justifyContent: 'center',
            marginTop: '20px',
        }
    };
};