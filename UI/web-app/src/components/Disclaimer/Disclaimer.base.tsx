import React, { useState, useEffect } from 'react';
import { Checkbox, PrimaryButton, Stack, Text, Modal, classNamesFunction, IProcessedStyleSet } from '@fluentui/react';
import { IDisclaimerProps, IDisclaimerStyleProps, IDisclaimerStyles } from './Disclaimer.types';
import { useStrings } from '../../store/hooks';
import { IStrings } from '../../services/localization/IStrings';

const getClassNames = classNamesFunction<
  IDisclaimerStyleProps,
  IDisclaimerStyles
>();

export const DisclaimerBase: React.FC<IDisclaimerProps> = (props: IDisclaimerProps) => {
    const { className, styles, checkboxes, onDismiss } = props;
    const strings: IStrings = useStrings();
    const classNames: IProcessedStyleSet<IDisclaimerStyles> = getClassNames(
        styles,
        {
            className,
        }
    );
    const [checkboxStates, setCheckboxStates] = useState(() => {
        const isSubmitted = localStorage.getItem('disclaimerSubmitted') === 'true';
        return checkboxes.reduce((acc, checkbox) => {
            acc[checkbox.id] = isSubmitted;
            return acc;
        }, {} as Record<string, boolean>);
    });

    useEffect(() => {
        localStorage.setItem('disclaimerCheckboxStates', JSON.stringify(checkboxStates));
    }, [checkboxStates]);

    const [isSubmitted, setIsSubmitted] = useState(() => {
        return localStorage.getItem('disclaimerSubmitted') === 'true';
    });

    const allChecked = Object.values(checkboxStates).every(Boolean);

    const handleCheckboxChange = (id: keyof IStrings['Disclaimer']) => {
        setCheckboxStates((prevState: Record<string, boolean>) => ({
            ...prevState,
            [id]: !prevState[id],
        }));
    };

    const handleSubmit = () => {
        localStorage.setItem('disclaimerSubmitted', 'true');
        setCheckboxStates(checkboxes.reduce((acc, checkbox) => {
            acc[checkbox.id] = true;
            return acc;
        }, {} as Record<string, boolean>));
        if (onDismiss) {
            onDismiss();
        }
    };

    return (
        <Modal
            isOpen={true}
            isBlocking={true}
            onDismiss={onDismiss}
        >
            <div className={classNames.modalContainer}>
                <Stack tokens={{ childrenGap: 20 }}>
                    <Text variant="large">{strings.Disclaimer.title}</Text>
                    {checkboxes.map((checkbox) => (
                        <Checkbox
                            key={checkbox.id}
                            label={strings.Disclaimer[checkbox.id as keyof IStrings['Disclaimer']]}
                            checked={checkboxStates[checkbox.id]}
                            onChange={() => handleCheckboxChange(checkbox.id as keyof IStrings['Disclaimer'])}
                        />
                    ))}
                    <div className={classNames.buttonContainer}>
                        <PrimaryButton 
                            text={strings.Disclaimer.submitButton} 
                            disabled={!allChecked} 
                            onClick={handleSubmit}
                        />
                    </div>
                </Stack>
            </div>
        </Modal>
    );
};