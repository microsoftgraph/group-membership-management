// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import {
  Checkbox, DefaultButton, Dialog, DialogFooter, DialogType, IconButton,
  MessageBar, PrimaryButton, TextField, TooltipHost, format, mergeStyleSets, useTheme,
} from '@fluentui/react';
import { useStrings } from '../../store/hooks';
import { getStyles } from './CreateGroup.styles';

interface AdvancedGroupSettingsProps {
  groupName: string;
  onDismiss: () => void;
}

export const AdvancedGroupSettings: React.FunctionComponent<AdvancedGroupSettingsProps> = ({ groupName, onDismiss }) => {
  const strings = useStrings();
  const labels = strings.ManageMembership.CreateGroup;
  const theme = useTheme();
  const classNames = mergeStyleSets(getStyles({ theme }));
  const previewId = React.useId();
  const infoId = React.useId();
  const [authorizedSenders, setAuthorizedSenders] = React.useState('');
  const [hideFromAddressList, setHideFromAddressList] = React.useState(false);
  const [enableSecurity, setEnableSecurity] = React.useState(false);
  const [allowExternalSenders, setAllowExternalSenders] = React.useState(false);

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

  return (
    <Dialog
      hidden={false}
      onDismiss={onDismiss}
      minWidth="min(676px, calc(100vw - 32px))"
      maxWidth={676}
      dialogContentProps={{
        type: DialogType.normal,
        title: labels.advancedTitle,
        closeButtonAriaLabel: strings.close,
        styles: {
          title: { padding: '28px 64px 24px 28px' },
          inner: { padding: '0 28px 28px' },
        },
      }}
      modalProps={{
        isBlocking: true,
        isAlert: false,
        focusTrapZoneProps: { firstFocusableTarget: root => root.querySelector('input') },
        styles: { main: { width: 'calc(100vw - 32px)', borderRadius: 8, maxHeight: 'calc(100vh - 32px)' } },
      }}
    >
      <div className={classNames.root}>
        <p style={{ margin: 0 }}>{format(labels.advancedDescription, groupName)}</p>
        <MessageBar id={previewId} role="none" delayedRender={false}>{labels.previewDisclosure}</MessageBar>
        <TextField
          label={labels.authorizedSenders}
          onRenderLabel={(props, defaultRender) => (
            <div className={classNames.label}>
              {defaultRender?.(props)}
              {renderInfo(labels.authorizedSenders, labels.authorizedSendersToolTip, 'senders')}
            </div>
          )}
          placeholder={labels.authorizedSendersPlaceholder}
          value={authorizedSenders}
          onChange={(_, value) => setAuthorizedSenders(value ?? '')}
          aria-describedby={previewId}
          styles={{ fieldGroup: classNames.textField }}
        />
        <div className={classNames.label}>
          <Checkbox label={labels.hideFromAddressList} checked={hideFromAddressList} onChange={(_, value) => setHideFromAddressList(!!value)} aria-describedby={previewId} />
          {renderInfo(labels.hideFromAddressList, labels.hideFromAddressListToolTip, 'address-list')}
        </div>
        <div className={classNames.label}>
          <Checkbox label={labels.enableSecurity} checked={enableSecurity} onChange={(_, value) => setEnableSecurity(!!value)} aria-describedby={previewId} />
          {renderInfo(labels.enableSecurity, labels.enableSecurityToolTip, 'security')}
        </div>
        <div className={classNames.label}>
          <Checkbox label={labels.allowExternalSenders} checked={allowExternalSenders} onChange={(_, value) => setAllowExternalSenders(!!value)} aria-describedby={previewId} />
          {renderInfo(labels.allowExternalSenders, labels.allowExternalSendersToolTip, 'external-senders')}
        </div>
      </div>
      <DialogFooter>
        <PrimaryButton text={labels.applySettings} onClick={onDismiss} aria-describedby={previewId} />
        <DefaultButton text={labels.notNow} onClick={onDismiss} />
      </DialogFooter>
    </Dialog>
  );
};
