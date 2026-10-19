// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { useEffect, useRef, useState } from 'react';
import {
  Checkbox, DefaultButton, Dialog, DialogFooter, DialogType, Dropdown,
  IconButton, IProcessedStyleSet, IRenderFunction, MessageBar, MessageBarType, PrimaryButton, Spinner,
  TextField, TooltipHost, classNamesFunction, format, useTheme,
} from '@fluentui/react';
import { useSelector } from 'react-redux';
import { ICreateGroupProps, ICreateGroupStyleProps, ICreateGroupStyles } from './CreateGroup.types';
import { useStrings } from '../../store/hooks';
import {
  manageMembershipCreatedGroupId, manageMembershipCreateGroupErrorMessage,
  manageMembershipCreateGroupLoading, manageMembershipCreateGroupUncertain,
} from '../../store/manageMembership.slice';
import { NewGroupType } from '../../models/NewGroupType';
import { buildCreateGroupRequest, deriveGroupAlias } from './createGroupPayload';

const getClassNames = classNamesFunction<ICreateGroupStyleProps, ICreateGroupStyles>();

export const CreateGroupBase: React.FunctionComponent<ICreateGroupProps> = props => {
  const { className, styles, groupType, onGroupCreated, onDismiss } = props;
  const strings = useStrings();
  const labels = strings.ManageMembership.CreateGroup;
  const theme = useTheme();
  const classNames: IProcessedStyleSet<ICreateGroupStyles> = getClassNames(styles, { className, theme });
  const [groupName, setGroupName] = useState('');
  const [groupAlias, setGroupAlias] = useState('');
  const [description, setDescription] = useState('');
  const [useNameAsAlias, setUseNameAsAlias] = useState(true);
  const [sensitivityLabel, setSensitivityLabel] = useState<string | null>(null);
  const [visibility, setVisibility] = useState('private');
  const [policy, setPolicy] = useState<string | null>(null);
  const [welcomeEmails, setWelcomeEmails] = useState(false);
  const [autoFollow, setAutoFollow] = useState(true);
  const [showInOutlook, setShowInOutlook] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [localUncertain, setLocalUncertain] = useState(false);
  const submissionGuard = useRef(false);
  const errorRef = useRef<HTMLDivElement>(null);
  const previewId = React.useId();
  const labelsHelpId = React.useId();
  const securityHelpId = React.useId();
  const infoId = React.useId();
  const isSecurityGroup = groupType === NewGroupType.Security;
  const loading = useSelector(manageMembershipCreateGroupLoading);
  const errorMessage = useSelector(manageMembershipCreateGroupErrorMessage);
  const createdGroupId = useSelector(manageMembershipCreatedGroupId);
  const uncertain = useSelector(manageMembershipCreateGroupUncertain) || localUncertain;
  const pending = submitting || loading;
  const requiredFieldsMissing = !groupName.trim() || (!isSecurityGroup && !groupAlias.trim());

  const renderInfo = (label: string, description: string, key: string) => (
    <TooltipHost content={description} id={`${infoId}-${key}`} calloutProps={{ gapSpace: 0 }} tooltipProps={{ role: 'tooltip' }}>
      <IconButton
        iconProps={{ iconName: 'Info' }}
        ariaLabel={format(strings.Components.InfoIcon.ariaLabel, label)}
        aria-describedby={`${infoId}-${key}`}
        styles={{ root: { color: theme.palette.themePrimary, flexShrink: 0 } }}
      />
    </TooltipHost>
  );

  const renderLabel = <T,>(label: string, description: string, key: string): IRenderFunction<T> =>
    (fieldProps, defaultRender) => (
      <div className={classNames.label}>
        {defaultRender?.(fieldProps)}
        {renderInfo(label, description, key)}
      </div>
    );

  useEffect(() => {
    if (!pending && (errorMessage || uncertain)) errorRef.current?.scrollIntoView({ block: 'nearest' });
  }, [errorMessage, uncertain, pending]);

  const dismiss = () => {
    if (!submissionGuard.current && !loading) onDismiss();
  };

  const submit = async () => {
    if (submissionGuard.current || loading || uncertain || createdGroupId || requiredFieldsMissing) return;
    submissionGuard.current = true;
    setSubmitting(true);
    const payload = buildCreateGroupRequest({ groupName, groupAlias, groupType, description });
    try {
      await onGroupCreated(payload.groupName, payload.groupAlias, payload.groupType, description);
    } catch {
      // A callback failure is not proof that the server did not create the group.
      setLocalUncertain(true);
    } finally {
      submissionGuard.current = false;
      setSubmitting(false);
    }
  };

  return (
    <Dialog
      hidden={false}
      onDismiss={dismiss}
      minWidth="min(676px, calc(100vw - 32px))"
      maxWidth={676}
      dialogContentProps={{
        type: DialogType.normal,
        title: isSecurityGroup ? labels.securityTitle : labels.microsoft365Title,
        showCloseButton: false,
        topButtonsProps: [{
          iconProps: { iconName: 'Cancel' },
          ariaLabel: strings.close,
          disabled: pending,
          onClick: dismiss,
        }],
        styles: {
          title: { padding: '28px 64px 24px 28px' },
          inner: { padding: '0 28px 28px' },
        },
      }}
      modalProps={{
        isBlocking: pending,
        isAlert: false,
        styles: { main: { width: 'calc(100vw - 32px)', borderRadius: 8, maxHeight: 'calc(100vh - 32px)' } },
      }}
    >
      <div className={classNames.root} aria-busy={pending}>
        <TextField
          label={labels.groupName}
          onRenderLabel={isSecurityGroup ? undefined : renderLabel(labels.groupName, labels.groupNameToolTip, 'name')}
          placeholder={labels.groupNamePlaceholder}
          styles={{ fieldGroup: classNames.textField }}
          required
          value={groupName}
          onChange={(_, value) => {
            const name = value ?? '';
            setGroupName(name);
            if (useNameAsAlias) setGroupAlias(deriveGroupAlias(name));
          }}
          disabled={pending}
        />
        {!isSecurityGroup && (
          <div className={classNames.alias}>
            <TextField
              label={labels.groupAlias}
              onRenderLabel={renderLabel(labels.groupAlias, labels.groupAliasToolTip, 'alias')}
              placeholder={labels.groupAliasPlaceholder}
              styles={{ fieldGroup: classNames.textField }}
              required
              value={groupAlias}
              onChange={(_, value) => {
                setGroupAlias(value ?? '');
                setUseNameAsAlias(false);
              }}
              disabled={pending}
            />
            <Checkbox
              label={labels.useNameAsAlias}
              checked={useNameAsAlias}
              onChange={(_, checked) => {
                setUseNameAsAlias(!!checked);
                if (checked) setGroupAlias(deriveGroupAlias(groupName));
              }}
              disabled={pending}
            />
          </div>
        )}
        <TextField
          label={labels.description}
          onRenderLabel={isSecurityGroup ? undefined : renderLabel(labels.description, labels.descriptionToolTip, 'description')}
          placeholder={labels.descriptionPlaceholder}
          styles={{ fieldGroup: classNames.textField }}
          multiline
          rows={3}
          value={description}
          onChange={(_, value) => setDescription(value ?? '')}
          disabled={pending}
        />
        <MessageBar id={previewId} role="none" delayedRender={false}>{labels.previewDisclosure}</MessageBar>
        <div>
          <Dropdown
            label={labels.sensitivityLabel}
            onRenderLabel={isSecurityGroup ? undefined : renderLabel(labels.sensitivityLabel, labels.sensitivityLabelToolTip, 'sensitivity')}
            ariaLabel={labels.sensitivityLabel}
            placeholder={labels.sensitivityPlaceholder}
            selectedKey={sensitivityLabel}
            options={[
              { key: 'confidential-example', text: labels.exampleConfidential },
              { key: 'general-example', text: labels.exampleGeneral },
            ]}
            onChange={(_, option) => setSensitivityLabel(option ? String(option.key) : null)}
            aria-describedby={`${previewId} ${labelsHelpId}${isSecurityGroup ? ` ${securityHelpId}` : ''}`}
            disabled={pending}
          />
          <p id={labelsHelpId} className={classNames.help}>{labels.exampleLabelsHelp}</p>
          {isSecurityGroup && <p id={securityHelpId} className={classNames.help}>{labels.securityLabelHelp}</p>}
        </div>
        {isSecurityGroup ? (
          <Dropdown
            label={labels.policy}
            ariaLabel={labels.policy}
            placeholder={labels.policyPlaceholder}
            selectedKey={policy}
            options={[
              { key: 'owner-approval', text: labels.ownerApprovalPolicy },
              { key: 'owner-and-admins', text: labels.ownerAndAdminsPolicy },
            ]}
            onChange={(_, option) => setPolicy(option ? String(option.key) : null)}
            aria-describedby={previewId}
            disabled={pending}
          />
        ) : (
          <>
            <Dropdown
              label={labels.visibility}
              onRenderLabel={renderLabel(labels.visibility, labels.visibilityToolTip, 'visibility')}
              ariaLabel={labels.visibility}
              placeholder={labels.visibilityPlaceholder}
              selectedKey={visibility}
              options={[
                { key: 'private', text: labels.privateVisibility },
                { key: 'public', text: labels.publicVisibility },
              ]}
              onChange={(_, option) => { if (option) setVisibility(String(option.key)); }}
              aria-describedby={previewId}
              disabled={pending}
            />
            {groupName && <p className={classNames.help}>{labels.namingExample}</p>}
            <div className={classNames.label}>
              <Checkbox label={labels.welcomeEmails} checked={welcomeEmails} onChange={(_, value) => setWelcomeEmails(!!value)} disabled={pending} aria-describedby={previewId} />
              {renderInfo(labels.welcomeEmails, labels.welcomeEmailsToolTip, 'welcome')}
            </div>
            <div className={classNames.label}>
              <Checkbox label={labels.autoFollow} checked={autoFollow} onChange={(_, value) => setAutoFollow(!!value)} disabled={pending} aria-describedby={previewId} />
              {renderInfo(labels.autoFollow, labels.autoFollowToolTip, 'follow')}
            </div>
            <div className={classNames.label}>
              <Checkbox label={labels.showInOutlook} checked={showInOutlook} onChange={(_, value) => setShowInOutlook(!!value)} disabled={pending} aria-describedby={previewId} />
              {renderInfo(labels.showInOutlook, labels.showInOutlookToolTip, 'outlook')}
            </div>
          </>
        )}
        {pending && <div role="status"><Spinner label={labels.creating} /></div>}
        {!pending && (errorMessage || uncertain) && (
          <div ref={errorRef}>
            <MessageBar messageBarType={MessageBarType.error} role="alert" delayedRender={false}>
              {uncertain ? labels.uncertainMessage : labels.failureMessage}
            </MessageBar>
          </div>
        )}
      </div>
      <DialogFooter>
        <DefaultButton text={strings.cancel} onClick={dismiss} disabled={pending} />
        <PrimaryButton text={pending ? labels.creating : labels.createGroup} onClick={submit} disabled={pending || uncertain || !!createdGroupId || requiredFieldsMissing} />
      </DialogFooter>
    </Dialog>
  );
};
