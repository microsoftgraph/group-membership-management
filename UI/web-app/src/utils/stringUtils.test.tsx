// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { render } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { jsxFormat, renderMultilineHeader } from './stringUtils';

describe('jsxFormat', () => {
  it('returns empty fragment when template is undefined', () => {
    const result = jsxFormat(undefined as unknown as string);
    const { container } = render(<>{result}</>);
    expect(container.textContent).toBe('');
  });

  it('returns empty fragment when template is empty string', () => {
    const result = jsxFormat('');
    const { container } = render(<>{result}</>);
    expect(container.textContent).toBe('');
  });

  it('returns plain text when no placeholders are present', () => {
    const result = jsxFormat('Hello world');
    const { container } = render(<>{result}</>);
    expect(container.textContent).toBe('Hello world');
  });

  it('replaces a single placeholder with a component', () => {
    const result = jsxFormat('Click {0} to continue', <a href="#">here</a>);
    const { container } = render(<>{result}</>);
    expect(container.textContent).toBe('Click here to continue');
    expect(container.querySelector('a')).not.toBeNull();
  });

  it('replaces multiple placeholders', () => {
    const result = jsxFormat('{0} and {1}', <b>first</b>, <i>second</i>);
    const { container } = render(<>{result}</>);
    expect(container.textContent).toBe('first and second');
    expect(container.querySelector('b')).not.toBeNull();
    expect(container.querySelector('i')).not.toBeNull();
  });
});

describe('renderMultilineHeader', () => {
  it('renders single word as a span', () => {
    const { container } = render(renderMultilineHeader('Status'));
    expect(container.querySelector('span')?.textContent).toBe('Status');
  });

  it('renders multiple words each in their own div', () => {
    const { container } = render(renderMultilineHeader('Start Date'));
    const wrapper = container.querySelector('div[style]')!;
    const wordDivs = wrapper.querySelectorAll(':scope > div');
    expect(wordDivs).toHaveLength(2);
    expect(wordDivs[0].textContent).toBe('Start');
    expect(wordDivs[1].textContent).toBe('Date');
  });
});
