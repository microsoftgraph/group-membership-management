// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
    classNamesFunction,
    IProcessedStyleSet,
    Panel,
    PanelType,
    TextField,
    IconButton,
    Spinner,
    SpinnerSize,
    ProgressIndicator,
    useTheme,
    PrimaryButton,
    DefaultButton,
} from '@fluentui/react';
import { v4 as uuidv4 } from 'uuid';
import {
    ICopilotPanelProps,
    ICopilotPanelStyleProps,
    ICopilotPanelStyles,
    IChatMessage,
    ISuggestedPrompt,
} from './CopilotPanel.types';
import { useStrings } from '../../store/hooks';
import { useDispatch, useSelector } from 'react-redux';
import { AppDispatch } from '../../store';
import {
    selectCopilotMessages,
    selectCopilotConversationId,
    selectCopilotIsLoading,
    selectCopilotError,
    selectLastSourceParts,
    selectLastAppliedOperations,
    selectUseOrgStructure,
    selectCopilotWarning,
    addMessage,
    clearMessages,
    clearLastSourcePart,
    setError,
} from '../../store/copilot.slice';
import { sendCopilotMessage, UserContext } from '../../store/copilot.api';
import { selectCopilotSuggestedPrompts } from '../../store/settings.slice';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { SourcePartType } from '../../models/SourcePartType';
import { CopilotAvatar } from './CopilotAvatar';
import { getSourcePartsFromState } from '../../store/manageMembership.slice';
import { ISourcePart } from '../../models/ISourcePart';
import { extractProposedMembership, formatMembershipDescription } from '../../utils/copilotMembershipSummary';

const getMembershipSnapshot = (parts: ISourcePart[]) =>
    JSON.stringify(parts.map(({ id, query }) => ({ id, query })));

const getClassNames = classNamesFunction<
    ICopilotPanelStyleProps,
    ICopilotPanelStyles
>();

export const CopilotPanelBase: React.FunctionComponent<ICopilotPanelProps> = (
    props: ICopilotPanelProps
) => {
    const {
        className,
        styles,
        isOpen,
        dismissPanel,
        onSourcePartsGenerated,
        sourcePartId,
        hrAttributes,
        experience = 'onboarding',
    } = props;
    const isUpdateExperience = experience === 'update';
    const strings = useStrings();
    const theme = useTheme();
    const dispatch = useDispatch<AppDispatch>();

    const classNames: IProcessedStyleSet<ICopilotPanelStyles> = getClassNames(styles, {
        className,
        theme,
        experience,
    });

    const messages = useSelector(selectCopilotMessages);
    const conversationId = useSelector(selectCopilotConversationId);
    const isLoading = useSelector(selectCopilotIsLoading);
    const error = useSelector(selectCopilotError);
    const lastSourceParts = useSelector(selectLastSourceParts);
    const lastAppliedOperations = useSelector(selectLastAppliedOperations);
    const useOrgStructure = useSelector(selectUseOrgStructure);
    const warning = useSelector(selectCopilotWarning);
    const sourceParts = useSelector(getSourcePartsFromState);

    // Index of the most recent non-error message. Apply & Review anchors here so a
    // previously-proposed change stays acceptable even after a failed turn — but only
    // when everything after it is an error (a later user/bot turn would move this index
    // forward, correctly retiring the offer).
    const lastNonErrorIndex = useMemo(() => {
        for (let i = messages.length - 1; i >= 0; i--) {
            if (!messages[i].isError) return i;
        }
        return -1;
    }, [messages]);

    const [inputValue, setInputValue] = useState('');
    const [showUpdateResumePrompt, setShowUpdateResumePrompt] = useState(false);
    const [resumeMessageIndex, setResumeMessageIndex] = useState<number | null>(null);
    const [membershipSummaryRequested, setMembershipSummaryRequested] = useState(false);
    const [showMembershipSummaryActions, setShowMembershipSummaryActions] = useState(false);
    const [isRefreshingMembership, setIsRefreshingMembership] = useState(false);
    const [updateIntro, setUpdateIntro] = useState<'none' | 'reset'>('none');
    const [appliedMembership, setAppliedMembership] = useState<{
        summary: string;
        isSummary: boolean;
        snapshot: string;
    } | null>(null);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLDivElement>(null);
    const wasLoadingRef = useRef(false);
    const wasOpenRef = useRef(false);
    const applyGenerationRef = useRef(0);

    // Updates always offer resume; onboarding offers it once a conversation exists.
    useEffect(() => {
        if (isOpen && !wasOpenRef.current) {
            if (isUpdateExperience || appliedMembership || messages.length > 0) {
                setUpdateIntro('none');
                setResumeMessageIndex(messages.length);
                setShowUpdateResumePrompt(true);
                setShowMembershipSummaryActions(false);
                const snapshot = getMembershipSnapshot(sourceParts);
                const needsInitialDescription = isUpdateExperience && !appliedMembership &&
                    messages.length === 0 && sourceParts.length > 0;
                if ((needsInitialDescription || (appliedMembership && appliedMembership.snapshot !== snapshot)) && !isRefreshingMembership) {
                    const generation = applyGenerationRef.current;
                    setIsRefreshingMembership(true);
                    dispatch(sendCopilotMessage({
                        message: 'What is my current membership?',
                        purpose: 'resumeMembershipSummary',
                        hrAttributes,
                    })).then(result => {
                        if (generation !== applyGenerationRef.current) return;
                        setIsRefreshingMembership(false);
                        if (sendCopilotMessage.fulfilled.match(result)) {
                            setAppliedMembership({
                                summary: result.payload.message.content,
                                isSummary: true,
                                snapshot,
                            });
                        }
                    });
                }
            }
        }
        if (!isOpen) {
            setShowUpdateResumePrompt(false);
            setResumeMessageIndex(null);
        }
        wasOpenRef.current = isOpen;
    }, [isOpen, isUpdateExperience, messages.length, appliedMembership, sourceParts, isRefreshingMembership, dispatch, hrAttributes]);

    // Re-focus input after AI response completes
    useEffect(() => {
        if (wasLoadingRef.current && !isLoading) {
            // Small delay to ensure the DOM is updated
            setTimeout(() => {
                const textarea = inputRef.current?.querySelector('textarea');
                textarea?.focus();
            }, 100);
        }
        wasLoadingRef.current = isLoading;
    }, [isLoading]);

    // Suggested prompts from admin settings
    const suggestedPromptsJson = useSelector(selectCopilotSuggestedPrompts);
    const suggestedPrompts: ISuggestedPrompt[] = useMemo(() => {
        if (!suggestedPromptsJson) return [];
        try {
            const parsed = JSON.parse(suggestedPromptsJson);
            if (Array.isArray(parsed)) {
                return parsed
                    .filter((p: any) => typeof p === 'object' && p !== null)
                    .map((p: any, i: number) => ({
                        id: String(i + 1),
                        label: typeof p.label === 'string' ? p.label : '',
                        prompt: typeof p.prompt === 'string' ? p.prompt : '',
                    })).filter((p: ISuggestedPrompt) => p.label && p.prompt);
            }
        } catch { /* invalid JSON, show no prompts */ }
        return [];
    }, [suggestedPromptsJson]);

    const showSuggestedPrompts = !membershipSummaryRequested && messages.length === 0 && suggestedPrompts.length > 0;

    // Scroll to bottom when new messages arrive
    useEffect(() => {
        messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }, [messages]);

    // Scroll to bottom when panel opens (returning to existing conversation)
    useEffect(() => {
        if (isOpen && messages.length > 0) {
            setTimeout(() => {
                messagesEndRef.current?.scrollIntoView({ behavior: 'auto' });
            }, 50);
        }
    }, [isOpen]);

    const handleSendMessage = useCallback(async (messageText: string, displayText?: string) => {
        if (!messageText.trim() || isLoading) return;

        const userMessage: IChatMessage = {
            id: uuidv4(),
            role: 'user',
            content: (displayText || messageText).trim(),
            timestamp: new Date().toISOString(),
        };

        dispatch(addMessage(userMessage));
        setInputValue('');
        setShowUpdateResumePrompt(false);
        setShowMembershipSummaryActions(false);

        const userContext: UserContext = {
            managerName: undefined,
            managerEmail: undefined,
            managerAlias: undefined,
        };

        try {
            await dispatch(sendCopilotMessage({ 
                message: messageText.trim(), 
                userContext,
                hrAttributes,
            })).unwrap();
        } catch (err) {
            // Error is handled by the slice
        }
    }, [dispatch, isLoading, hrAttributes]);

    const handleAcceptAndApply = useCallback(async () => {
        if (lastSourceParts.length === 0 || !onSourcePartsGenerated) return;

        const response = messages[lastNonErrorIndex]?.content ?? '';
        const generation = ++applyGenerationRef.current;
        // Invalidates any in-flight refresh, whose handler would otherwise never clear this flag.
        setIsRefreshingMembership(false);
        try {
            // Keep the proposal and panel until the apply succeeds so a failure can be retried.
            const appliedParts = await onSourcePartsGenerated(lastSourceParts);
            if (generation !== applyGenerationRef.current) return;
            dispatch(clearLastSourcePart());
            dismissPanel();
            dispatch(addMessage({
                id: uuidv4(),
                role: 'user',
                content: strings.Copilot.applyAndReview,
                timestamp: new Date().toISOString(),
                action: 'applyAndReview',
            }));
            // The existing response contract puts the complete proposal in this paragraph.
            const summary = extractProposedMembership(response);
            setAppliedMembership({
                summary: summary ? formatMembershipDescription(summary) : response,
                isSummary: !!summary,
                snapshot: getMembershipSnapshot(appliedParts ?? lastSourceParts),
            });
        } catch (error) {
            const message = error instanceof Error ? error.message : strings.Copilot.errorMessage;
            dispatch(addMessage({ id: uuidv4(), role: 'assistant', content: message, timestamp: new Date().toISOString(), isError: true }));
            dispatch(setError(message));
        }
    }, [lastSourceParts, onSourcePartsGenerated, dismissPanel, dispatch, messages, lastNonErrorIndex, strings.Copilot.errorMessage, strings.Copilot.applyAndReview]);

    const handleSuggestedPromptClick = useCallback((prompt: string, label: string) => {
        handleSendMessage(prompt, label);
    }, [handleSendMessage]);

    const handleInputKeyDown = useCallback((event: React.KeyboardEvent<HTMLInputElement | HTMLTextAreaElement>) => {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            handleSendMessage(inputValue);
        }
    }, [handleSendMessage, inputValue]);

    const handleNewConversation = useCallback(() => {
        const generation = ++applyGenerationRef.current;
        dispatch(clearMessages());
        setInputValue('');
        setShowUpdateResumePrompt(false);
        setResumeMessageIndex(null);
        setIsRefreshingMembership(false);
        setUpdateIntro('reset');
        const hasMembership = sourceParts.length > 0;
        const reusableMembership = hasMembership && appliedMembership?.isSummary &&
            appliedMembership.snapshot === getMembershipSnapshot(sourceParts)
            ? appliedMembership
            : null;
        setAppliedMembership(reusableMembership);
        setMembershipSummaryRequested(hasMembership);
        setShowMembershipSummaryActions(hasMembership);
        if (reusableMembership) {
            dispatch(addMessage({
                id: uuidv4(),
                role: 'assistant',
                content: `**${strings.Copilot.currentMembership}**\n${reusableMembership.summary}`,
                timestamp: new Date().toISOString(),
                isMembershipSummary: true,
            }));
        } else if (hasMembership) {
            const snapshot = getMembershipSnapshot(sourceParts);
            dispatch(sendCopilotMessage({
                message: 'What is my current membership?',
                purpose: 'membershipSummary',
                hrAttributes,
            })).then(result => {
                if (generation === applyGenerationRef.current && sendCopilotMessage.fulfilled.match(result)) {
                    setAppliedMembership({
                        summary: result.payload.message.content,
                        isSummary: true,
                        snapshot,
                    });
                }
            });
        }
    }, [dispatch, sourceParts, appliedMembership, hrAttributes, strings.Copilot.currentMembership]);

    const copiedTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const isMountedRef = useRef(true);
    const [copyStatus, setCopyStatus] = useState<'idle' | 'success' | 'error'>('idle');

    useEffect(() => {
        isMountedRef.current = true;
        return () => {
            isMountedRef.current = false;
            if (copiedTimeoutRef.current) {
                clearTimeout(copiedTimeoutRef.current);
                copiedTimeoutRef.current = null;
            }
        };
    }, []);

    const handleCopyConversation = useCallback(async () => {
        if (messages.length === 0 && !error && !warning) {
            return;
        }
        // Build a diagnostic payload that mirrors everything visible in the chat
        // window plus cross-referencing metadata. Beyond the message list we also
        // include the session (conversation) id for log correlation and the current
        // error/warning banners — which are surfaced from Redux state rather than as
        // chat messages, so they were previously absent from the copied text (e.g. a
        // "please try again" failure would not appear in the copy).
        const diagnostics = {
            sessionId: conversationId,
            copiedAt: new Date().toISOString(),
            messageCount: messages.length,
            messages,
            error: error ?? null,
            warning: warning ?? null,
        };
        const payload = JSON.stringify(diagnostics, null, 2);
        try {
            await navigator.clipboard.writeText(payload);
            if (!isMountedRef.current) {
                return;
            }
            setCopyStatus('success');
            if (copiedTimeoutRef.current) {
                clearTimeout(copiedTimeoutRef.current);
            }
            copiedTimeoutRef.current = setTimeout(() => {
                if (!isMountedRef.current) {
                    return;
                }
                setCopyStatus('idle');
                copiedTimeoutRef.current = null;
            }, 1500);
        } catch (err) {
            // eslint-disable-next-line no-console
            console.error('Failed to copy conversation', err);
            if (!isMountedRef.current) {
                return;
            }
            setCopyStatus('error');
            if (copiedTimeoutRef.current) {
                clearTimeout(copiedTimeoutRef.current);
            }
            copiedTimeoutRef.current = setTimeout(() => {
                if (!isMountedRef.current) {
                    return;
                }
                setCopyStatus('idle');
                copiedTimeoutRef.current = null;
            }, 2000);
        }
    }, [messages, conversationId, error, warning]);

    const handleResumeContinue = useCallback(() => {
        setShowUpdateResumePrompt(false);
        setShowMembershipSummaryActions(false);
        inputRef.current?.querySelector('textarea')?.focus();
    }, []);

    const renderBotAvatar = () => <CopilotAvatar />;

    const renderResumeActions = () => (
        <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
            <PrimaryButton
                text={strings.Copilot.resumeContinue}
                onClick={handleResumeContinue}
                styles={{ root: { flex: 1 } }}
            />
            <DefaultButton
                text={strings.Copilot.newConversation}
                onClick={handleNewConversation}
                styles={{ root: { flex: 1 } }}
            />
        </div>
    );

    const onRenderHeader = (): JSX.Element => {
        return (
            <div className={classNames.header} data-testid="copilot-header">
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: isUpdateExperience ? '10px' : '6px' }}>
                        {renderBotAvatar()}
                        <div>
                            <div className={classNames.headerTitle}>
                                {strings.Copilot?.title || 'GMM Copilot'}
                            </div>
                            <div className={classNames.headerSubtitle}>
                                {strings.Copilot?.subtitle || 'Your AI-powered membership builder'}
                            </div>
                        </div>
                    </div>
                    <div style={{ display: 'flex', gap: '4px', alignItems: 'center' }}>
                        {!isUpdateExperience && (messages.length > 0 || error || warning) && <IconButton
                            iconProps={{ 
                                iconName: copyStatus === 'success' ? 'CheckMark' 
                                    : copyStatus === 'error' ? 'StatusErrorFull' 
                                    : 'Copy' 
                            }}
                            title={copyStatus === 'success'
                                ? (strings.Copilot?.copiedConversation || 'Copied!')
                                : copyStatus === 'error'
                                    ? (strings.Copilot?.copyFailed || 'Copy failed')
                                    : (strings.Copilot?.copyConversation || 'Copy conversation')}
                            ariaLabel={strings.Copilot?.copyConversation || 'Copy conversation'}
                            onClick={handleCopyConversation}
                            disabled={messages.length === 0 && !error && !warning}
                            styles={{
                                root: {
                                    color: copyStatus === 'error' 
                                        ? theme.semanticColors.errorIcon 
                                        : theme.palette.neutralSecondary,
                                    height: '28px',
                                    width: '28px',
                                },
                                rootHovered: {
                                    color: theme.palette.themePrimary,
                                    backgroundColor: theme.palette.neutralLighter,
                                },
                            }}
                        />}
                        {/* Screen reader announcement for copy status */}
                        <span 
                            role="status" 
                            aria-live="polite" 
                            style={{ 
                                position: 'absolute', 
                                width: '1px', 
                                height: '1px', 
                                padding: 0, 
                                margin: '-1px', 
                                overflow: 'hidden', 
                                clip: 'rect(0, 0, 0, 0)', 
                                whiteSpace: 'nowrap', 
                                border: 0 
                            }}
                        >
                            {copyStatus === 'success' 
                                ? (strings.Copilot?.copiedConversation || 'Copied!') 
                                : copyStatus === 'error' 
                                    ? (strings.Copilot?.copyFailed || 'Copy failed') 
                                    : ''}
                        </span>
                        {messages.length > 0 && (
                            <DefaultButton
                                iconProps={{ iconName: 'CommentAdd' }}
                                text={strings.Copilot.newConversation}
                                ariaLabel={strings.Copilot.newConversation}
                                onClick={handleNewConversation}
                                styles={{
                                    root: {
                                        height: 28,
                                        width: 92,
                                        minWidth: 92,
                                        flexShrink: 0,
                                        padding: '0 8px',
                                        borderColor: theme.palette.themePrimary,
                                        borderRadius: 16,
                                        fontSize: 12,
                                        color: theme.palette.neutralPrimary,
                                        backgroundColor: theme.palette.themeLighter,
                                    },
                                    label: { fontSize: 12, fontWeight: 400, whiteSpace: 'nowrap', margin: 0 },
                                    icon: { color: theme.palette.themePrimary, margin: '0 4px 0 0' },
                                }}
                            />
                        )}
                        <IconButton
                            iconProps={{ iconName: 'Cancel' }}
                            title={strings.Copilot?.closeButton || 'Close'}
                            ariaLabel={strings.Copilot?.closeButton || 'Close'}
                            onClick={dismissPanel}
                            styles={{
                                root: {
                                    color: theme.palette.neutralSecondary,
                                    height: '28px',
                                    width: '28px',
                                },
                                rootHovered: {
                                    color: theme.palette.neutralPrimary,
                                    backgroundColor: theme.palette.neutralLighter,
                                },
                            }}
                        />
                    </div>
                </div>
            </div>
        );
    };

    const renderWelcomeMessage = (): JSX.Element | null => {
        if (membershipSummaryRequested) return null;
        if (isUpdateExperience) {
            if (updateIntro === 'none' || showUpdateResumePrompt) return null;

            return (
                <div
                    className={`${classNames.messageRow} ${classNames.botMessage}`}
                    data-testid="copilot-update-welcome"
                >
                    {renderBotAvatar()}
                    <div style={{ width: '100%' }}>
                        <div className={classNames.welcomeCard}>
                            <div style={{ fontWeight: 600, fontSize: '14px', lineHeight: '24px' }}>
                                {strings.Copilot.updateWelcomeTitle}
                            </div>
                            <div style={{ fontSize: '14px', lineHeight: '24px' }}>
                                {strings.Copilot.updateWelcomeDescription}
                            </div>
                        </div>
                        {updateIntro === 'reset' && showSuggestedPrompts && (
                            <div className={classNames.suggestedPromptsContainer}>
                                {suggestedPrompts.map((prompt) => (
                                    <button
                                        key={prompt.id}
                                        className={classNames.suggestedPromptButton}
                                        onClick={() => handleSuggestedPromptClick(prompt.prompt, prompt.label)}
                                        disabled={isLoading}
                                        type="button"
                                    >
                                        {prompt.label}
                                    </button>
                                ))}
                            </div>
                        )}
                    </div>
                </div>
            );
        }

        return (
            <div
                className={`${classNames.messageRow} ${classNames.botMessage}`}
                style={{ flexShrink: 0 }}
                data-testid="copilot-onboarding-welcome"
            >
                {renderBotAvatar()}
                <div style={{ width: '100%' }}>
                    <div className={classNames.welcomeCard}>
                        <div style={{ fontWeight: 600, fontSize: '14px', lineHeight: '24px', color: theme.palette.neutralPrimary }}>
                            {strings.Copilot?.welcomeMessage || "Hi, I'm GMM Copilot."}
                        </div>
                        <div style={{ fontSize: '14px', lineHeight: '24px', color: theme.palette.neutralPrimary }}>
                            {strings.Copilot?.welcomeDescription || "Describe your membership and I'll build the rules for you to review. You'll have everything ready in no time."}
                        </div>
                        <div className={classNames.welcomeStepList}>
                            <div className={classNames.welcomeStep}>
                                <div className={classNames.welcomeStepNumber}>1</div>
                                <div className={classNames.welcomeStepText}>
                                    <span>{strings.Copilot?.welcomeStep1Bold || 'Tell me what you need.'}</span>
                                    {strings.Copilot?.welcomeStep1 || ' Pick a suggestion below to start with or type your own membership description.'}
                                </div>
                            </div>
                            <div className={classNames.welcomeStep}>
                                <div className={classNames.welcomeStepNumber}>2</div>
                                <div className={classNames.welcomeStepText}>
                                    <span>{strings.Copilot?.welcomeStep2Bold || "I'll ask a few questions"}</span>
                                    {strings.Copilot?.welcomeStep2 || ' to fill in any details needed.'}
                                </div>
                            </div>
                            <div className={classNames.welcomeStep}>
                                <div className={classNames.welcomeStepNumber}>3</div>
                                <div className={classNames.welcomeStepText}>
                                    <span>{strings.Copilot?.welcomeStep3Bold || 'Review and confirm.'}</span>
                                    {strings.Copilot?.welcomeStep3 || " I'll show all the filters built based on your description before anything is saved."}
                                </div>
                            </div>
                        </div>
                        <div className={classNames.welcomeDisclaimer} data-testid="copilot-disclaimer">
                            {strings.Copilot.welcomeDisclaimer}
                        </div>
                    </div>
                    {showSuggestedPrompts && (
                    <div className={classNames.suggestedPromptsContainer}>
                        {suggestedPrompts.map((prompt) => (
                            <button
                                key={prompt.id}
                                className={classNames.suggestedPromptButton}
                                    onClick={() => handleSuggestedPromptClick(prompt.prompt, prompt.label)}
                                disabled={isLoading}
                                type="button"
                            >
                                {prompt.label}
                            </button>
                        ))}
                    </div>
                    )}
                </div>
            </div>
        );
    };

    const renderUpdateResumePrompt = (): JSX.Element | null => {
        if (resumeMessageIndex === null) {
            return null;
        }

        return (
            <div
                className={`${classNames.messageRow} ${classNames.botMessage}`}
                data-testid="copilot-update-resume"
            >
                {renderBotAvatar()}
                <div className={classNames.messageContent} style={{ width: '100%', boxSizing: 'border-box' }}>
                    <div style={{ marginBottom: 8 }}><strong>{strings.Copilot?.updateResumeTitle || 'Welcome back.'}</strong></div>
                    {isRefreshingMembership
                        ? <ProgressIndicator label={strings.Copilot.gettingCurrentMembership} />
                        : appliedMembership && (
                        <div data-testid="copilot-applied-summary">
                            {appliedMembership.snapshot !== getMembershipSnapshot(sourceParts)
                                ? strings.Copilot.appliedMembershipChanged
                                : <>
                                    <div><strong>{appliedMembership.isSummary
                                        ? strings.Copilot.currentMembership
                                        : strings.Copilot.lastAppliedResponse}</strong></div>
                                    <div data-testid="copilot-membership-description" style={{ fontWeight: appliedMembership.isSummary ? 700 : undefined }}>
                                        {renderMarkdown(appliedMembership.summary)}
                                    </div>
                                </>}
                        </div>
                    )}
                    {!isRefreshingMembership && <div>
                        {appliedMembership
                            ? strings.Copilot.appliedResumeDescription
                            : strings.Copilot.updateResumeDescription}
                    </div>}
                    {showUpdateResumePrompt && !isRefreshingMembership && renderResumeActions()}
                </div>
            </div>
        );
    };

    const renderMarkdown = (text: string): JSX.Element => {
        // Split by newlines first, then render inline formatting per line
        const lines = text.split('\n');

        const renderInline = (lineText: string, lineKey: number): JSX.Element => {
            const parts: JSX.Element[] = [];
            let remaining = lineText;
            let key = 0;

            while (remaining.length > 0) {
                const boldMatch = remaining.match(/\*\*([^*]+)\*\*/);
                const codeMatch = remaining.match(/`([^`]+)`/);

                const boldIndex = boldMatch ? remaining.indexOf(boldMatch[0]) : -1;
                const codeIndex = codeMatch ? remaining.indexOf(codeMatch[0]) : -1;

                if (boldIndex === -1 && codeIndex === -1) {
                    parts.push(<span key={`${lineKey}-${key++}`}>{remaining}</span>);
                    break;
                }

                const useCode = codeIndex !== -1 && (boldIndex === -1 || codeIndex < boldIndex);
                const match = useCode ? codeMatch! : boldMatch!;
                const matchIndex = useCode ? codeIndex : boldIndex;

                if (matchIndex > 0) {
                    parts.push(<span key={`${lineKey}-${key++}`}>{remaining.substring(0, matchIndex)}</span>);
                }

                if (useCode) {
                    parts.push(
                        <code key={`${lineKey}-${key++}`} style={{ 
                            backgroundColor: 'rgba(0,0,0,0.05)', 
                            padding: '2px 6px', 
                            borderRadius: '4px',
                            fontFamily: 'Consolas, monospace',
                            fontSize: '0.9em'
                        }}>
                            {match[1]}
                        </code>
                    );
                } else {
                    parts.push(<strong key={`${lineKey}-${key++}`}>{match[1]}</strong>);
                }

                remaining = remaining.substring(matchIndex + match[0].length);
            }

            return <>{parts}</>;
        };

        const elements: JSX.Element[] = [];
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            const trimmed = line.trim();

            // Bullet point line (- item)
            if (trimmed.startsWith('- ')) {
                elements.push(
                    <div key={`line-${i}`} style={{ paddingLeft: '12px', display: 'flex', gap: '6px' }}>
                        <span>•</span>
                        <span>{renderInline(trimmed.substring(2), i)}</span>
                    </div>
                );
            } else if (trimmed === '') {
                // Empty line = spacing
                elements.push(<div key={`line-${i}`} style={{ height: '8px' }} />);
            } else {
                elements.push(<div key={`line-${i}`}>{renderInline(line, i)}</div>);
            }
        }

        return <>{elements}</>;
    };

    const renderMessage = (message: IChatMessage, index: number): JSX.Element => {
        // Inline failure entry (spec: failed turns live in the transcript). Render the same
        // friendly error bubble the standalone banner used to show; the raw error string is
        // preserved in message.content for "Copy conversation" / log correlation.
        if (message.isError) {
            return (
                <div
                    key={message.id}
                    className={`${classNames.messageRow} ${classNames.botMessage}`}
                >
                    {renderBotAvatar()}
                    <div className={classNames.messageContent} style={{ backgroundColor: theme.semanticColors.errorBackground, color: theme.semanticColors.errorText }}>
                        <div>{strings.Copilot?.errorMessage || 'Sorry, something went wrong. Please try again.'}</div>
                        <div data-testid="copilot-load-fallback" style={{ marginTop: '6px' }}>
                            {strings.Copilot?.loadFailed || 'You can still create a new query from scratch here.'}
                        </div>
                    </div>
                </div>
            );
        }

        const isBot = message.role === 'assistant';
        const isLastBotMessage = isBot && index === lastNonErrorIndex;
        // Show Apply & Review only if this is the last bot message, we have a resulting query,
        // AND the server actually applied at least one operation this turn. A no-op turn
        // (describe/clarify/refuse) returns the query unchanged with no applied operations, so
        // offering "Apply & Review" there would re-apply an identical query and mislead the user.
        const canAccept = !message.isMembershipSummary && isLastBotMessage && lastSourceParts.length > 0 && lastAppliedOperations.length > 0 && !isLoading;
        
        const isApplyAction = message.action === 'applyAndReview';
        const compactUserMessage = isUpdateExperience || isApplyAction;
        const displayContent = isApplyAction ? strings.Copilot.applyAndReview : message.content;
        
        return (
            <div
                key={message.id}
                className={`${classNames.messageRow} ${isBot ? classNames.botMessage : classNames.userMessage}`}
                data-testid={message.isMembershipSummary ? 'copilot-current-membership' : isApplyAction ? 'copilot-apply-action' : undefined}
            >
                {isBot && (
                    renderBotAvatar()
                )}
                <div
                    style={{
                        display: 'flex',
                        flexDirection: 'column',
                        gap: '8px',
                        width: isBot ? '100%' : undefined,
                    }}
                >
                    <div 
                        className={classNames.messageContent} 
                        style={!isBot ? { 
                            backgroundColor: compactUserMessage ? theme.palette.themeLighter : theme.palette.themePrimary,
                            color: compactUserMessage ? theme.palette.neutralPrimary : theme.palette.white,
                            borderRadius: compactUserMessage ? '6px' : '20px',
                            padding: compactUserMessage ? '4px 8px' : '10px 20px',
                            fontSize: '14px',
                            lineHeight: compactUserMessage ? '20px' : undefined,
                            fontWeight: compactUserMessage ? 400 : 500,
                            wordBreak: 'break-word'
                        } : {}}
                    >
                        {message.isMembershipSummary
                            ? <div data-testid="copilot-membership-description" style={{ fontWeight: 700 }}>
                                {renderMarkdown(displayContent)}
                            </div>
                            : isBot ? renderMarkdown(displayContent) : displayContent}
                        {message.isMembershipSummary && (
                            <>
                                <div>{strings.Copilot.appliedResumeDescription}</div>
                                {showMembershipSummaryActions && isLastBotMessage && renderResumeActions()}
                            </>
                        )}
                        {canAccept && (
                            <PrimaryButton
                                className={classNames.inlineAcceptButton}
                                text={strings.Copilot.applyAndReview}
                                onClick={handleAcceptAndApply}
                            />
                        )}
                    </div>
                </div>
            </div>
        );
    };

    return (
        <Panel
            type={PanelType.custom}
            customWidth="529px"
            isOpen={isOpen}
            onDismiss={dismissPanel}
            hasCloseButton={false}
            onRenderHeader={onRenderHeader}
            isLightDismiss
            styles={{
                main: {
                    width: '529px',
                    maxWidth: '100vw',
                },
                commands: { paddingTop: 0 },
                scrollableContent: { display: 'flex', flexDirection: 'column', height: '100%' },
                content: { display: 'flex', flexDirection: 'column', flex: 1, padding: 0, overflow: 'hidden' },
                header: { padding: 0 },
            }}
        >
            <div className={classNames.root}>
                <div className={classNames.chatContainer}>
                    <div className={classNames.messagesContainer} data-testid="copilot-transcript">
                        {renderWelcomeMessage()}
                        {messages.map((msg, idx) => (
                            <React.Fragment key={msg.id}>
                                {idx === resumeMessageIndex && renderUpdateResumePrompt()}
                                {renderMessage(msg, idx)}
                            </React.Fragment>
                        ))}
                        {resumeMessageIndex === messages.length && renderUpdateResumePrompt()}

                        {isLoading && !isRefreshingMembership && (
                            <div
                                className={classNames.loadingIndicator}
                                role="status"
                            >
                                {membershipSummaryRequested && messages.length === 0
                                    ? <ProgressIndicator
                                        label={strings.Copilot.gettingCurrentMembership}
                                        styles={{ root: { width: '100%' } }}
                                    />
                                    : <>
                                        <Spinner size={SpinnerSize.small} />
                                        <span>{strings.Copilot?.thinking || 'Thinking...'}</span>
                                    </>}
                            </div>
                        )}
                        {warning && !error && (
                            <div className={`${classNames.messageRow} ${classNames.botMessage}`} data-testid="copilot-warning">
                                {renderBotAvatar()}
                                <div className={classNames.messageContent} style={{ backgroundColor: theme.semanticColors.warningBackground }}>
                                    {warning}
                                </div>
                            </div>
                        )}
                        <div ref={messagesEndRef} />
                    </div>
                    <div className={classNames.inputContainer} data-testid="copilot-composer">
                        <TextField
                            elementRef={inputRef}
                            className={classNames.inputField}
                            placeholder={strings.Copilot?.inputPlaceholder || 'Ask a question or describe the membership you want'}
                            value={inputValue}
                            onChange={(_, newValue) => setInputValue(newValue || '')}
                            onKeyDown={handleInputKeyDown}
                            disabled={isLoading}
                            multiline
                            rows={1}
                            autoAdjustHeight
                            resizable={false}
                            borderless
                            styles={{
                                root: {
                                    border: `1px solid ${theme.palette.neutralTertiary}`,
                                    borderRadius: '4px',
                                    padding: '4px 8px',
                                    paddingRight: '40px',
                                    borderBottomColor: isUpdateExperience ? undefined : theme.palette.themePrimary,
                                    minHeight: '40px',
                                    boxSizing: 'border-box',
                                },
                                fieldGroup: { minHeight: 0 },
                                field: {
                                    fontSize: '14px',
                                    minHeight: '20px',
                                    maxHeight: '100px',
                                    overflow: 'auto',
                                    lineHeight: '20px',
                                    padding: '4px 2px',
                                },
                            }}
                        />
                        <IconButton
                            className={classNames.sendButton}
                            iconProps={{ iconName: 'Send' }}
                            title={strings.Copilot?.sendButton || 'Send'}
                            ariaLabel={strings.Copilot?.sendButton || 'Send'}
                            onClick={() => handleSendMessage(inputValue)}
                            disabled={isLoading || !inputValue.trim()}
                            styles={{
                                root: {
                                    backgroundColor: 'transparent',
                                    borderRadius: '4px',
                                    marginLeft: '-40px',
                                    zIndex: 1,
                                },
                                rootDisabled: {
                                    backgroundColor: 'transparent',
                                },
                                icon: {
                                    color: theme.palette.neutralSecondary,
                                },
                            }}
                        />
                    </div>
                    <div className={classNames.disclaimer} data-testid="copilot-composer-disclaimer">
                        {strings.Copilot?.disclaimer || 'AI-generated content may be incorrect.'}
                    </div>
                </div>
            </div>
        </Panel>
    );
};
