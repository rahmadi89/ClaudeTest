import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AtmStatusBadge, atmTone, commandTone } from './StatusBadge';

describe('StatusBadge', () => {
  it('always renders a text label (status is never color-only)', () => {
    render(<AtmStatusBadge status="OutOfService" />);
    expect(screen.getByText('Out of service')).toBeInTheDocument();
  });

  it('maps statuses to the reserved status tones', () => {
    expect(atmTone('Online')).toBe('good');
    expect(atmTone('Offline')).toBe('critical');
    expect(commandTone('Rejected')).toBe('serious');
  });
});
