import { useEffect, useState } from 'react';
import { fetchAttachmentObjectUrl } from '../chatApi';

interface AttachmentImageProps {
  attachmentId: string;
  fileName: string;
  onOpen?: (url: string) => void;
}

// Chat attachments require a bearer header, so the raw endpoint cannot be used as an <img src>.
// Fetch the bytes as a blob and render an object URL, revoking it on unmount.
export default function AttachmentImage({ attachmentId, fileName, onOpen }: AttachmentImageProps) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let active = true;
    let created: string | null = null;

    fetchAttachmentObjectUrl(attachmentId)
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

  if (failed) {
    return <span className="rgchat-convo__sub">Unable to load image</span>;
  }

  if (!objectUrl) {
    return <span className="rgchat-convo__sub">Loading image…</span>;
  }

  return (
    <img
      className="rgchat-bubble__img"
      src={objectUrl}
      alt={fileName}
      onClick={() => onOpen?.(objectUrl)}
    />
  );
}
