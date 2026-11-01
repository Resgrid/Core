import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// planning widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'calendar.agenda': lazy(() => import('./CalendarAgendaWidget')),
  'shifts.today': lazy(() => import('./ShiftsTodayWidget')),
  'shifts.mine': lazy(() => import('./ShiftsMineWidget')),
  'shifts.approvals': lazy(() => import('./ShiftApprovalsWidget')),
  'shifts.coverage': lazy(() => import('./ShiftCoverageWidget')),
  'trainings.mine': lazy(() => import('./TrainingsMineWidget')),
  'documents.recent': lazy(() => import('./DocumentsRecentWidget')),
  'notes.board': lazy(() => import('./NotesBoardWidget')),
  'contacts.preplans': lazy(() => import('./ContactsPreplansWidget')),
  'links.quick': lazy(() => import('./QuickLinksWidget')),
  'general.stickyNote': lazy(() => import('./StickyNoteWidget')),
};

export default widgets;
