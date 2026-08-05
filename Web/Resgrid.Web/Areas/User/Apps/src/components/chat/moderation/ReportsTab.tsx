import ModerationRequestsTable from './ModerationRequestsTable';
import { moderationText } from '../moderationI18n';

export default function ReportsTab() {
  return (
    <div>
      <p className="rgchat-convo__sub">
        {moderationText('ReportsDescription')}
      </p>
      <ModerationRequestsTable reportMode />
    </div>
  );
}
