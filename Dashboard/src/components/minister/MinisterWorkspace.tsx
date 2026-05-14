import { findMinisterView, type MinisterViewContext } from '../../dashboard/ministerViewRegistry';
import type { MinisterViewKey } from '../../dashboard/scopes';

export function MinisterWorkspace({
  selectedView,
  ...context
}: MinisterViewContext & {
  selectedView: MinisterViewKey;
}) {
  return findMinisterView(selectedView).render(context);
}
