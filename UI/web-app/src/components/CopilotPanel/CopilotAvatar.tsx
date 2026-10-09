// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import copilotMark from '../../assets/copilot-mark.svg';
import copilotMarkCard from '../../assets/copilot-mark-card.svg';
import copilotMarkPerson from '../../assets/copilot-mark-person.svg';

export const CopilotAvatar: React.FunctionComponent = () => (
    <div
        style={{ position: 'relative', width: 32, height: 32, flexShrink: 0 }}
        aria-hidden="true"
        data-testid="copilot-avatar"
    >
        <img src={copilotMark} alt="" width={32} height={32} />
        <div style={{ position: 'absolute', left: 6, top: 0, width: 29, height: 28 }}>
            <div
                style={{
                    position: 'absolute',
                    left: 3.42,
                    top: 6.08,
                    width: 15,
                    height: 19,
                    borderRadius: 2,
                    backgroundColor: 'rgba(255, 255, 255, 0.8)',
                }}
            />
            <img
                src={copilotMarkCard}
                alt=""
                width={19}
                height={21}
                style={{ position: 'absolute', left: 0.42, top: 5.08 }}
            />
            <img
                src={copilotMarkPerson}
                alt=""
                width={12}
                height={15}
                style={{ position: 'absolute', left: 4.42, top: 8.08 }}
            />
        </div>
    </div>
);
