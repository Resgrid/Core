import { useState } from 'react';
import { getBrowserConfig } from '../../../runtime/browserConfig';
import { colorFor, initialsFor } from '../chatFormat';

interface AvatarProps {
  name: string | null | undefined;
  userId?: string | null;
  size?: 'sm' | 'md';
  online?: boolean;
  showPresence?: boolean;
}

export default function Avatar({ name, userId, size = 'md', online, showPresence }: AvatarProps) {
  const [imageFailed, setImageFailed] = useState(false);
  const classes = ['rgchat-avatar'];
  if (size === 'sm') {
    classes.push('rgchat-avatar--sm');
  }

  const avatarUrl =
    userId && userId.length > 0 && !imageFailed
      ? `${getBrowserConfig().apiBaseUrl}/api/v4/Avatars/Get?id=${encodeURIComponent(userId)}`
      : null;

  return (
    <span className={classes.join(' ')} style={{ backgroundColor: colorFor(userId ?? name) }}>
      {avatarUrl ? (
        <img className="rgchat-avatar__img" src={avatarUrl} alt="" onError={() => setImageFailed(true)} />
      ) : (
        initialsFor(name)
      )}
      {showPresence && (
        <span className={`rgchat-avatar__dot${online ? ' rgchat-avatar__dot--online' : ''}`} />
      )}
    </span>
  );
}
