import * as React from 'react';
import { useState, useEffect } from 'react';
import { IProcessedStyleSet, classNamesFunction, useTheme } from '@fluentui/react';
import { IGroupSettingProps, IGroupSettingStyleProps, IGroupSettingStyles } from "./GroupSetting.types";
import { AppDispatch } from '../../store';
import { useDispatch, useSelector } from 'react-redux';
import { useStrings } from '../../store/hooks';
import { NormalPeoplePicker, DirectionalHint, Label, TooltipHost, IconButton, Toggle } from '@fluentui/react';
import { IPersonaProps } from '@fluentui/react/lib/Persona';
import { AuthorizedSender } from '../../models/AuthorizedSender';
import { getPeoplePickerSuggestions } from '../../store/jobs.api';
import { searchDestinations as searchGroups } from '../../store/manageMembership.api';
import { GroupSettings } from '../../models/GroupSettings';
import { setGroupSettings } from '../../store/manageMembership.slice';
import { manageMembershipGroupSettings } from '../../store/manageMembership.slice';

const getClassNames = classNamesFunction<IGroupSettingStyleProps, IGroupSettingStyles>();

export const GroupSettingBase: React.FunctionComponent<IGroupSettingProps> = (props) => {
    const { className, styles } = props;
    const strings = useStrings();
    const dispatch = useDispatch<AppDispatch>();
    const classNames: IProcessedStyleSet<IGroupSettingStyles> = getClassNames(styles, {
        className,
        theme: useTheme(),
    });

    const groupSettings = useSelector(manageMembershipGroupSettings);

    function removeDuplicates(personas: IPersonaProps[], possibleDupes: IPersonaProps[]) {
        return personas.filter(persona => !listContainsPersona(persona, possibleDupes));
    }

    function listContainsPersona(persona: IPersonaProps, personas: IPersonaProps[]) {
        if (!personas || !personas.length || personas.length === 0) {
            return false;
        }
        return personas.filter(item => item.text === persona.text).length > 0;
    }

    const mapAuthorizedSenderToPersonaProps = (authorizedSender: AuthorizedSender | undefined): IPersonaProps => {
        if (!authorizedSender) return {};

        if (!authorizedSender.id) {
            return {};
        }

        return {
            key: authorizedSender.id,
            id: authorizedSender.id,
            imageUrl: authorizedSender.imageUrl,
            text: authorizedSender.displayName,
            secondaryText: authorizedSender.mail,
        };
    };

    const selectedItems = (): IPersonaProps[] => {
        const senders: IPersonaProps[] = [];
        groupSettings?.authorizedSenders?.forEach((authorizedSender) => {
            const sender = mapAuthorizedSenderToPersonaProps(authorizedSender);
            senders.push(sender);
        });

        return senders;
    }

    const getPickerSuggestions = async (
        filterText: string,
        currentPersonas: IPersonaProps[] | undefined
    ): Promise<IPersonaProps[]> => {
        if (!filterText || filterText.trim() === '') {
            return [];
        }

        const [users, groups] = await Promise.all([
            dispatch(getPeoplePickerSuggestions(filterText)),
            dispatch(searchGroups(filterText)),
        ]);

        const results = [...(users.payload as IPersonaProps[]), ...(groups.payload as IPersonaProps[])];
        const newPersonas = results.map((item, index) => {
            return {
                ...item,
                key: index
            };
        });

        const filteredPersonas = removeDuplicates(newPersonas, currentPersonas || []);
        return filteredPersonas;
    };

    const handleAuthorizedSendersChange = (items?: IPersonaProps[] | undefined) => {
        let newAuthorizedSenders: AuthorizedSender[] = [];
        if (items !== undefined && items.length > 0) {
            items.filter((item) => {
                if (item.id) {
                    const newAuthorizedSender: AuthorizedSender = {
                        id: item.id,
                        displayName: item.text || '',
                        mail: item.secondaryText || '',
                        imageUrl: item.imageUrl || ''
                    };
                    newAuthorizedSenders.push(newAuthorizedSender);
                }
            });

            updateGroupSettings(newAuthorizedSenders, undefined);
        }
    }

    const handleToggleChange = (event: React.MouseEvent<HTMLElement>, checked?: boolean) => {
        updateGroupSettings(undefined, checked);
    }

    const updateGroupSettings = (authorizedSenders: AuthorizedSender[] | undefined, hiddenFromExchangeClients: boolean | undefined) => {

        let newGroupSettings: GroupSettings
        if (!groupSettings) {
            newGroupSettings = {
                authorizedSenders: [],
                hiddenFromExchangeClients: true,
                welcomeMessageEnabled: false
            };
        }
        else {
            newGroupSettings = { ...groupSettings };
        }

        if (authorizedSenders !== undefined) {
            newGroupSettings.authorizedSenders = authorizedSenders;
        }

        if (hiddenFromExchangeClients !== undefined) {
            newGroupSettings.hiddenFromExchangeClients = hiddenFromExchangeClients;
        }

        dispatch(setGroupSettings(newGroupSettings));
    }

    return (
        <>
            <div className={classNames.labelContainer}>
                <Label>{strings.ManageMembership.CreateGroup.authorizedSenders}</Label>
                <TooltipHost content={strings.ManageMembership.CreateGroup.authorizedSendersToolTip} id="toolTipAuthorizedSenders" calloutProps={{ gapSpace: 0 }}>
                    <IconButton title={strings.ManageMembership.CreateGroup.authorizedSendersToolTip} iconProps={{ iconName: "Info" }} aria-describedby="toolTipAuthorizedSenders" />
                </TooltipHost>
            </div>
            <NormalPeoplePicker
                aria-label={strings.ManageMembership.CreateGroup.authorizedSenders}
                onResolveSuggestions={getPickerSuggestions}
                onChange={handleAuthorizedSendersChange}
                key={'normal'}
                resolveDelay={300}
                styles={{ root: classNames.textField, text: classNames.textFieldGroup }}
                pickerCalloutProps={{ directionalHint: DirectionalHint.bottomAutoEdge, calloutWidth: 500 }}
                pickerSuggestionsProps={{ className: classNames.suggestionItems }}
                selectedItems={selectedItems()}
            />
            <div className={classNames.labelContainer}>
                <Label>{strings.ManageMembership.CreateGroup.hiddenFromExchangeClients}</Label>
                <TooltipHost content={strings.ManageMembership.CreateGroup.hiddenFromExchangeClientsToolTip} id="toolTipHiddenFromExchangeClients" calloutProps={{ gapSpace: 0 }}>
                    <IconButton title={strings.ManageMembership.CreateGroup.hiddenFromExchangeClientsToolTip} iconProps={{ iconName: "Info" }} aria-describedby="toolTipHiddenFromExchangeClients" />
                </TooltipHost>
            </div>
            <div className={classNames.toggleContainer}>
                <Toggle
                    inlineLabel
                    onChange={handleToggleChange}
                    checked={groupSettings?.hiddenFromExchangeClients ?? true}
                />
            </div>
            <div className={classNames.labelContainer}>
                <Label>{strings.ManageMembership.CreateGroup.welcomeMessageEnabled}</Label>
                <TooltipHost content={strings.ManageMembership.CreateGroup.welcomeMessageEnabledToolTip} id="toolTipWelcomeMessageEnabled" calloutProps={{ gapSpace: 0 }}>
                    <IconButton title={strings.ManageMembership.CreateGroup.welcomeMessageEnabledToolTip} iconProps={{ iconName: "Info" }} aria-describedby="toolTipWelcomeMessageEnabled" />
                </TooltipHost>
            </div>
            <div className={classNames.toggleContainer}>
                <Toggle
                    inlineLabel
                    checked={false}
                    disabled={true}
                />
            </div>
        </>
    )
}