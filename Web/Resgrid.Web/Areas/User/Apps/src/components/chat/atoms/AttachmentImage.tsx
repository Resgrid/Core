import { useEffect, useState } from 'react';
import { fetchAttachmentObjectUrl } from '../chatApi';

interface AttachmentImageProps {
  attachmentId: string;
  fileName: string;
  onOpen?: (url: string) => void;
}

// Chat attachments require a bearer header, so the raw endpoint cannot be used as an <img src>.
// Render the thumbnail inline as a blob object URL; fetch the full image lazily on click.
export default function AttachmentImage({ attachmentId, fileName, onOpen }: AttachmentImageProps) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [opening, setOpening] = useState(false);

  useEffect(() => {
    let active = true;
    let created: string | null = null;

    fetchAttachmentObjectUrl(attachmentId, true)
      .then((url) => {
        if (active) {
          created = url;
          setObjectUrl(url);
        } else {
          URL.revokeObjectURL(url);
        }
      })
      .catch(() => {
        if (active) {
          setFailed(true);
        }
      });

    return () => {
      active = false;
      if (created) {
        URL.revokeObjectURL(created);
      }
    };
  }, [attachmentId]);

  const handleOpen = () => {
    if (!onOpen || opening) {
      return;
    }
    setOpening(true);
    fetchAttachmentObjectUrl(attachmentId, false)
      .then((fullUrl) => onOpen(fullUrl))
      .catch(() => undefined)
      .finally(() => setOpening(false));
  };

  if (failed) {
    return <span className="rgchat-convo__sub">Unable to load image</span>;
  }

  if (!objectUrl) {
    return <span className="rgchat-skeleton rgchat-skeleton--img" aria-hidden="true" />;
  }

  return (
    <img
      className={`rgchat-bubble__img${opening ? ' rgchat-bubble__img--busy' : ''}`}
      src={objectUrl}
      alt={fileName}
      loading="lazy"
      decoding="async"
      onClick={handleOpen}
    />
  );
}
