import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// intelligence widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'ai.assistant': lazy(() => import('./AssistantWidget')),
  'aiDispatch.triage': lazy(() => import('./AiDispatchTriageWidget')),
};

export default widgets;
