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
    useTheme,
    Icon,
    Dialog,
    DialogType,
    DialogFooter,
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
    selectCopilotIsLoading,
    selectCopilotError,
    selectLastSourceParts,
    selectUseOrgStructure,
    addMessage,
    clearMessages,
    clearLastSourcePart,
} from '../../store/copilot.slice';
import { sendCopilotMessage, UserContext } from '../../store/copilot.api';
import { selectCopilotSuggestedPrompts } from '../../store/settings.slice';
import { selectOrgLeaderDetails } from '../../store/orgLeaderDetails.slice';
import { getSourcePartsFromState } from '../../store/manageMembership.slice';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { SourcePartType } from '../../models/SourcePartType';

const getClassNames = classNamesFunction<
    ICopilotPanelStyleProps,
    ICopilotPanelStyles
>();

export const CopilotPanelBase: React.FunctionComponent<ICopilotPanelProps> = (
    props: ICopilotPanelProps
) => {
    const { className, styles, isOpen, dismissPanel, onSourcePartsGenerated, sourcePartId, hrAttributes } = props;
    const strings = useStrings();
    const theme = useTheme();
    const dispatch = useDispatch<AppDispatch>();

    const classNames: IProcessedStyleSet<ICopilotPanelStyles> = getClassNames(styles, { className, theme });

    const messages = useSelector(selectCopilotMessages);
    const isLoading = useSelector(selectCopilotIsLoading);
    const error = useSelector(selectCopilotError);
    const lastSourceParts = useSelector(selectLastSourceParts);
    const useOrgStructure = useSelector(selectUseOrgStructure);
    const orgLeaderDetails = useSelector(selectOrgLeaderDetails);
    const sourceParts = useSelector(getSourcePartsFromState);

    // Build a rich context describing the current source part's full configuration
    const currentMembershipContext = React.useMemo(() => {
        if (!sourcePartId) return undefined;
        const part = sourceParts.find(p => p.id === sourcePartId);
        if (!part || part.query.type !== SourcePartType.HR) return undefined;
        const hrSource = part.query.source as HRSourcePartSource;

        const pieces: string[] = [];

        // SQL filter
        if (hrSource?.filter) {
            pieces.push(`filter: ${hrSource.filter}`);
        }

        // Exclusion
        if (part.query.exclusionary) {
            pieces.push('exclusionary: true');
        }

        // Org structure / manager / depth
        if (hrSource?.manager?.id) {
            pieces.push('orgStructure: enabled');
            // Try to get the display name from orgLeaderDetails if it matches
            if (orgLeaderDetails?.text) {
                pieces.push(`orgLeader: ${orgLeaderDetails.text}`);
            }
            if (hrSource.manager.depth != null) {
                pieces.push(`depth: ${hrSource.manager.depth}`);
            } else {
                pieces.push('depth: all levels');
            }
        }

        return pieces.length > 0 ? pieces.join(' | ') : undefined;
    }, [sourcePartId, sourceParts, orgLeaderDetails]);

    const [inputValue, setInputValue] = useState('');
    const [showResumeDialog, setShowResumeDialog] = useState(false);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLDivElement>(null);
    const wasLoadingRef = useRef(false);
    const wasOpenRef = useRef(false);

    // When the panel transitions from closed -> open and there are existing messages,
    // ask the user whether to continue the previous conversation or start over.
    useEffect(() => {
        if (isOpen && !wasOpenRef.current && messages.length > 0) {
            setShowResumeDialog(true);
        }
        if (!isOpen) {
            setShowResumeDialog(false);
        }
        wasOpenRef.current = isOpen;
    }, [isOpen, messages.length]);

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
                currentFilter: currentMembershipContext
            })).unwrap();
        } catch (err) {
            // Error is handled by the slice
        }
    }, [dispatch, isLoading, hrAttributes, currentMembershipContext]);

    const handleAcceptAndApply = useCallback(() => {
        if (lastSourceParts.length === 0 || !onSourcePartsGenerated) return;
        
        // Each source part already carries its own org leader info (useOrgStructure, managerToAutoSelect, depthToAutoSelect)
        // from the backend response — no need to assemble managerInfo here
        onSourcePartsGenerated(lastSourceParts);
        dispatch(clearLastSourcePart());
        dismissPanel();
    }, [lastSourceParts, onSourcePartsGenerated, dismissPanel, dispatch]);

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
        dispatch(clearMessages());
        setInputValue('');
    }, [dispatch]);

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
        if (messages.length === 0) {
            return;
        }
        const payload = JSON.stringify(messages, null, 2);
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
    }, [messages]);

    const handleResumeStartOver = useCallback(() => {
        dispatch(clearMessages());
        setInputValue('');
        setShowResumeDialog(false);
    }, [dispatch]);

    const handleResumeContinue = useCallback(() => {
        setShowResumeDialog(false);
    }, []);

    const renderBotAvatar = () => (
        <div className={classNames.botAvatar} style={{ position: 'relative' }}>
            <Icon iconName="Contact" style={{ color: '#ffffff', fontSize: '14px' }} />
            <svg
                width="14" height="14"
                viewBox="0 0 16 16"
                fill="none"
                style={{ position: 'absolute', top: '-5px', right: '-5px' }}
            >
                <path className={classNames.sparkleStar1} d="M8 4 L9 7 L12 8 L9 9 L8 12 L7 9 L4 8 L7 7 Z" fill="#0078d4" />
                <path className={classNames.sparkleStar2} d="M13 1 L13.5 2.5 L15 3 L13.5 3.5 L13 5 L12.5 3.5 L11 3 L12.5 2.5 Z" fill="#0078d4" />
            </svg>
        </div>
    );

    const onRenderHeader = (): JSX.Element => {
        return (
            <div className={classNames.header}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
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
                        <IconButton
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
                            disabled={messages.length === 0}
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
                        />
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
                            <IconButton
                                iconProps={{ iconName: 'EditNote' }}
                                title={strings.Copilot?.newConversation || 'New conversation'}
                                ariaLabel={strings.Copilot?.newConversation || 'New conversation'}
                                onClick={handleNewConversation}
                                styles={{
                                    root: {
                                        color: theme.palette.neutralSecondary,
                                        height: '28px',
                                        width: '28px',
                                    },
                                    rootHovered: {
                                        color: theme.palette.themePrimary,
                                        backgroundColor: theme.palette.neutralLighter,
                                    },
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

    const renderWelcomeMessage = (): JSX.Element => {
        return (
            <div className={`${classNames.messageRow} ${classNames.botMessage}`} style={{ width: '90%', maxWidth: '90%' }}>
                {renderBotAvatar()}
                <div>
                    <div className={classNames.welcomeCard}>
                        <div style={{ fontWeight: 600, fontSize: '14px', marginBottom: '4px', color: theme.palette.neutralPrimary }}>
                            {strings.Copilot?.welcomeMessage || "Hi, I'm GMM Copilot."}
                        </div>
                        <div style={{ fontSize: '13px', lineHeight: '18px', color: theme.palette.neutralSecondary }}>
                            {strings.Copilot?.welcomeDescription || "Describe your membership and I'll build the rules for you to review. You'll have everything ready in no time."}
                        </div>
                        <div className={classNames.welcomeStepList}>
                            <div className={classNames.welcomeStep}>
                                <div className={classNames.welcomeStepNumber}>1</div>
                                <div className={classNames.welcomeStepText}>
                                    <strong>{strings.Copilot?.welcomeStep1Bold || 'Tell me what you need.'}</strong>
                                    {strings.Copilot?.welcomeStep1 || ' Pick a suggestion below to start with or type your own membership description.'}
                                </div>
                            </div>
                            <div className={classNames.welcomeStep}>
                                <div className={classNames.welcomeStepNumber}>2</div>
                                <div className={classNames.welcomeStepText}>
                                    <strong>{strings.Copilot?.welcomeStep2Bold || "I'll ask a few questions"}</strong>
                                    {strings.Copilot?.welcomeStep2 || ' to fill in any details needed.'}
                                </div>
                            </div>
                            <div className={classNames.welcomeStep}>
                                <div className={classNames.welcomeStepNumber}>3</div>
                                <div className={classNames.welcomeStepText}>
                                    <strong>{strings.Copilot?.welcomeStep3Bold || 'Review and confirm.'}</strong>
                                    {strings.Copilot?.welcomeStep3 || " I'll show all the filters built based on your description before anything is saved."}
                                </div>
                            </div>
                        </div>
                    </div>
                    {suggestedPrompts.length > 0 && (
                    <div className={classNames.suggestedPromptsContainer}>
                        <div className={classNames.suggestedPromptsHeader}>
                            {strings.Copilot?.tryOneOfTheseToGetStarted || 'TRY ONE OF THESE TO GET STARTED'}
                        </div>
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
        const isBot = message.role === 'assistant';
        const isLastBotMessage = isBot && index === messages.length - 1;
        // Show Accept & Apply only if this is the last bot message AND we have a source part ready
        const canAccept = isLastBotMessage && lastSourceParts.length > 0 && !isLoading;
        
        const displayContent = message.content;
        
        return (
            <div
                key={message.id}
                className={`${classNames.messageRow} ${isBot ? classNames.botMessage : classNames.userMessage}`}
            >
                {isBot && (
                    renderBotAvatar()
                )}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', maxWidth: isBot ? '85%' : undefined }}>
                    <div 
                        className={classNames.messageContent} 
                        style={!isBot ? { 
                            backgroundColor: theme.palette.themePrimary, 
                            color: theme.palette.white,
                            borderRadius: '20px',
                            padding: '10px 20px',
                            fontSize: '14px',
                            fontWeight: 500,
                            wordBreak: 'break-word'
                        } : {}}
                    >
                        {isBot ? renderMarkdown(displayContent) : displayContent}
                    </div>
                    {canAccept && (
                        <>
                            <button
                                className={classNames.inlineAcceptButton}
                                onClick={handleAcceptAndApply}
                            >
                                <Icon iconName="CheckMark" />
                                {strings.Copilot?.acceptAndApply || 'Accept & Apply'}
                            </button>
                        </>
                    )}
                </div>
            </div>
        );
    };

    return (
        <Panel
            type={PanelType.medium}
            isOpen={isOpen}
            onDismiss={dismissPanel}
            hasCloseButton={false}
            onRenderHeader={onRenderHeader}
            isLightDismiss
            styles={{
                main: { maxWidth: '480px' },
                scrollableContent: { display: 'flex', flexDirection: 'column', height: '100%' },
                content: { display: 'flex', flexDirection: 'column', flex: 1, padding: 0, overflow: 'hidden' },
                header: { padding: 0 },
            }}
        >
            <Dialog
                hidden={!showResumeDialog}
                onDismiss={handleResumeContinue}
                dialogContentProps={{
                    type: DialogType.normal,
                    title: strings.Copilot?.resumeDialogTitle,
                    subText: strings.Copilot?.resumeDialogDescription,
                }}
                modalProps={{ isBlocking: true }}
            >
                <DialogFooter>
                    <DefaultButton
                        onClick={handleResumeStartOver}
                        text={strings.Copilot?.resumeDialogStartOver}
                    />
                    <PrimaryButton
                        onClick={handleResumeContinue}
                        text={strings.Copilot?.resumeDialogContinue}
                    />
                </DialogFooter>
            </Dialog>
            <div className={classNames.root}>
                <div className={classNames.chatContainer}>
                    <div className={classNames.messagesContainer}>
                        {renderWelcomeMessage()}
                        {messages.map((msg, idx) => renderMessage(msg, idx))}

                        {isLoading && (
                            <div className={classNames.loadingIndicator}>
                                <Spinner size={SpinnerSize.small} />
                                <span>{strings.Copilot?.thinking || 'Thinking...'}</span>
                            </div>
                        )}
                        {error && (
                            <div className={`${classNames.messageRow} ${classNames.botMessage}`}>
                                {renderBotAvatar()}
                                <div className={classNames.messageContent} style={{ backgroundColor: theme.semanticColors.errorBackground, color: theme.semanticColors.errorText }}>
                                    {strings.Copilot?.errorMessage || 'Sorry, something went wrong. Please try again.'}
                                </div>
                            </div>
                        )}
                        <div ref={messagesEndRef} />
                    </div>
                    <div className={classNames.inputContainer}>
                        <TextField
                            elementRef={inputRef}
                            className={classNames.inputField}
                            placeholder={strings.Copilot?.inputPlaceholder || 'Ask a question or describe the membership you want'}
                            value={inputValue}
                            onChange={(_, newValue) => setInputValue(newValue || '')}
                            onKeyDown={handleInputKeyDown}
                            disabled={isLoading}
                            multiline
                            autoAdjustHeight
                            resizable={false}
                            borderless
                            styles={{
                                root: {
                                    border: `1px solid ${theme.palette.neutralTertiary}`,
                                    borderRadius: '4px',
                                    padding: '4px 8px',
                                },
                                field: {
                                    fontSize: '14px',
                                    minHeight: '20px',
                                    maxHeight: '100px',
                                    overflow: 'auto',
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
                                    backgroundColor: theme.palette.themePrimary,
                                    borderRadius: '4px',
                                },
                                rootDisabled: {
                                    backgroundColor: theme.palette.neutralLighter,
                                },
                                icon: {
                                    color: theme.palette.white,
                                },
                            }}
                        />
                    </div>
                </div>
            </div>
        </Panel>
    );
};
