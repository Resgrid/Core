import { useEffect, useState } from 'react';
import { getSettings, updateSettings } from '../chatModerationApi';
import type { ChatSettingsDto } from '../types';

const DEFAULT_SETTINGS: ChatSettingsDto = {
  ChatDepartmentSettingId: null,
  RetentionDays: 0,
  AllowImages: true,
  AllowGifs: true,
  AllowLocationSharing: true,
  UrgentOverridesMute: true,
  MaxAttachmentSizeMb: 10,
  ChatbotEnabled: true,
};

interface ToggleRow {
  key: keyof ChatSettingsDto;
  label: string;
}

const TOGGLES: ToggleRow[] = [
  { key: 'AllowImages', label: 'Allow image attachments' },
  { key: 'AllowGifs', label: 'Allow GIFs' },
  { key: 'AllowLocationSharing', label: 'Allow location sharing' },
  { key: 'UrgentOverridesMute', label: 'Urgent messages override mute' },
  { key: 'ChatbotEnabled', label: 'Assistant (chatbot) enabled' },
];

export default function SettingsTab() {
  const [settings, setSettings] = useState<ChatSettingsDto>(DEFAULT_SETTINGS);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    getSettings()
      .then((result) => {
        if (result) {
          setSettings(result);
        }
      })
      .catch(() => undefined)
      .finally(() => setLoading(false));
  }, []);

  const save = async () => {
    setSaving(true);
    setSaved(false);
    try {
      const result = await updateSettings(settings);
      if (result) {
        setSettings(result);
        setSaved(true);
      }
    } catch (error) {
      console.error('Failed to save chat settings.', error);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return <div className="rgchat-convo__sub">Loading…</div>;
  }

  return (
    <div style={{ maxWidth: 480 }}>
      <div className="rgchat-form-row">
        <label htmlFor="rgchat-retention">Message retention (days, 0 = forever)</label>
        <input
          id="rgchat-retention"
          className="rgchat-input"
          type="number"
          min={0}
          style={{ width: 100 }}
          value={settings.RetentionDays}
          onChange={(event) => setSettings((current) => ({ ...current, RetentionDays: Number(event.target.value) }))}
        />
      </div>

      <div className="rgchat-form-row">
        <label htmlFor="rgchat-maxsize">Max attachment size (MB)</label>
        <input
          id="rgchat-maxsize"
          className="rgchat-input"
          type="number"
          min={1}
          style={{ width: 100 }}
          value={settings.MaxAttachmentSizeMb}
          onChange={(event) => setSettings((current) => ({ ...current, MaxAttachmentSizeMb: Number(event.target.value) }))}
        />
      </div>

      {TOGGLES.map((toggle) => (
        <div className="rgchat-form-row" key={toggle.key}>
          <label htmlFor={`rgchat-${toggle.key}`}>{toggle.label}</label>
          <input
            id={`rgchat-${toggle.key}`}
            type="checkbox"
            checked={Boolean(settings[toggle.key])}
            onChange={(event) => setSettings((current) => ({ ...current, [toggle.key]: event.target.checked }))}
          />
        </div>
      ))}

      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginTop: 12 }}>
        <button type="button" className="rgchat-btn rgchat-btn--primary" onClick={() => void save()} disabled={saving}>
          {saving ? 'Saving…' : 'Save settings'}
        </button>
        {saved && <span className="rgchat-pill rgchat-pill--done">Saved</span>}
      </div>
    </div>
  );
}
