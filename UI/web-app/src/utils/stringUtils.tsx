// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
import { Fragment, ReactNode } from 'react';

export function jsxFormat(template: string, ...components: ReactNode[]): ReactNode {
    const parts = template.split(/(\{\d+\})/g); // split by placeholders like {0}, {1}
    return (<>
        {parts.map((part, index) => {
          const match = part.match(/\{(\d+)\}/);
          if (match) {
            const idx = Number(match[1]);
            return <Fragment key={index}>{components[idx]}</Fragment>;
          }
          return <Fragment key={index}>{part}</Fragment>;
        })}
      </>
    );
  };

