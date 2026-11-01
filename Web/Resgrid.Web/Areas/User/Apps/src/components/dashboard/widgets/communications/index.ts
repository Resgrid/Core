import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// communications widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'messages.inbox': lazy(() => import('./MessagesInboxWidget')),
  'chat.unread': lazy(() => import('./ChatUnreadWidget')),
  'chat.moderation': lazy(() => import('./ChatModerationWidget')),
  'comms.tests': lazy(() => import('./CommunicationTestsWidget')),
  'voice.ptt': lazy(() => import('./VoicePttWidget')),
};

export default widgets;
