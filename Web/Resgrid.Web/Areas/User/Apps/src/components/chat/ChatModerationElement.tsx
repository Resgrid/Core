import { useState } from 'react';
import './chat.css';
import FlagsTab from './moderation/FlagsTab';
import ReportsTab from './moderation/ReportsTab';
import ActionsTab from './moderation/ActionsTab';
import SettingsTab from './moderation/SettingsTab';
import ExportsTab from './moderation/ExportsTab';
import { moderationText } from './moderationI18n';

type ModTab = 'requests' | 'reports' | 'actions' | 'settings' | 'exports';

export interface ChatModerationElementProps {
  hostElement?: HTMLElement;
  departmentAdmin?: boolean;
}

export default function ChatModerationElement({ departmentAdmin = false }: ChatModerationElementProps) {
  const [tab, setTab] = useState<ModTab>('requests');
  const sharedTabs: { key: ModTab; label: string }[] = [
    { key: 'requests', label: moderationText('TabRequests') },
    { key: 'reports', label: moderationText('TabReports') },
  ];
  const departmentTabs: { key: ModTab; label: string }[] = [
    { key: 'actions', label: moderationText('TabChatControls') },
    { key: 'settings', label: moderationText('TabChatSettings') },
    { key: 'exports', label: moderationText('TabChatExports') },
  ];
  const tabs = departmentAdmin ? [...sharedTabs, ...departmentTabs] : sharedTabs;

  return (
    <div className="rgchat-root">
      <div className="rgchat-mod">
        <div className="rgchat-mod__tabs">
          {tabs.map((item) => (
            <button
              key={item.key}
              type="button"
              className={`rgchat-mod__tab${tab === item.key ? ' rgchat-mod__tab--active' : ''}`}
              onClick={() => setTab(item.key)}
            >
              {item.label}
            </button>
          ))}
        </div>
        <div className="rgchat-mod__body">
          {tab === 'requests' && <FlagsTab />}
          {tab === 'reports' && <ReportsTab />}
          {tab === 'actions' && <ActionsTab />}
          {tab === 'settings' && <SettingsTab />}
          {tab === 'exports' && <ExportsTab />}
        </div>
      </div>
    </div>
  );
}
