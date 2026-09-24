import { useEffect, useRef, type ReactNode } from 'react';
import { ApiError } from '../api/client';

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return (
    <div className="pager">
      <span>{total.toLocaleString()} total</span>
      <button className="btn btn-sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>Previous</button>
      <span>Page {page} of {pages}</span>
      <button className="btn btn-sm" disabled={page >= pages} onClick={() => onPage(page + 1)}>Next</button>
    </div>
  );
}

export function ErrorBox({ error }: { error: unknown }) {
  if (!error) return null;
  const message = error instanceof ApiError || error instanceof Error ? error.message : 'Something went wrong.';
  return <div className="error-box" role="alert">{message}</div>;
}

export const Empty = ({ children }: { children: ReactNode }) => <div className="empty">{children}</div>;

export const Loading = () => <div className="empty" aria-busy="true">Loading…</div>;

/** Native modal dialog: focus trapping, Esc-to-close and backdrop come from the platform. */
export function Modal({ open, onClose, title, children }: { open: boolean; onClose: () => void; title: string; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) d.showModal();
    if (!open && d.open) d.close();
  }, [open]);
  return (
    <dialog ref={ref} onClose={onClose} aria-labelledby="modal-title">
      {open && (
        <div className="dialog-body">
          <h2 id="modal-title" style={{ margin: 0 }}>{title}</h2>
          {children}
        </div>
      )}
    </dialog>
  );
}
