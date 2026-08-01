import { useState } from 'react';
import './chat.css';
import FlagsTab from './moderation/FlagsTab';
import ActionsTab from './moderation/ActionsTab';
import SettingsTab from './moderation/SettingsTab';
import ExportsTab from './moderation/ExportsTab';

type ModTab = 'flags' | 'actions' | 'settings' | 'exports';

const TABS: { key: ModTab; label: string }[] = [
  { key: 'flags', label: 'Flags' },
  { key: 'actions', label: 'Actions log' },
  { key: 'settings', label: 'Settings' },
  { key: 'exports', label: 'Exports' },
];

export interface ChatModerationElementProps {
  hostElement?: HTMLElement;
}

export default function ChatModerationElement(_props: ChatModerationElementProps) {
  const [tab, setTab] = useState<ModTab>('flags');

  return (
    <div className="rgchat-root">
      <div className="rgchat-mod">
        <div className="rgchat-mod__tabs">
          {TABS.map((item) => (
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
          {tab === 'flags' && <FlagsTab />}
          {tab === 'actions' && <ActionsTab />}
          {tab === 'settings' && <SettingsTab />}
          {tab === 'exports' && <ExportsTab />}
        </div>
      </div>
    </div>
  );
}
