// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

const relativeLuminance = (color: string): number => {
  const hex = /^#([\da-f]{6})$/i.exec(color);
  const rgb = /^rgb\((\d+),\s*(\d+),\s*(\d+)\)$/.exec(color);
  const channels = hex
    ? [0, 2, 4].map((offset) => parseInt(hex[1].slice(offset, offset + 2), 16))
    : rgb?.slice(1).map(Number);
  if (!channels) {
    throw new Error(`Expected an opaque six-digit hex or RGB color, received ${color}`);
  }
  if (channels.some((channel) => channel > 255)) {
    throw new Error(`Color channel outside the RGB range: ${color}`);
  }

  const [r, g, b] = channels.map((channel) => {
    const c = channel / 255;
    return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};

export const contrastRatio = (foreground: string, background: string): number => {
  const l1 = relativeLuminance(foreground);
  const l2 = relativeLuminance(background);
  return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
};
