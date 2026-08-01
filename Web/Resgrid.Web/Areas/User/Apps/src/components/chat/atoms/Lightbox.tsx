import { useEffect } from 'react';

interface LightboxProps {
  url: string;
  alt?: string;
  onClose: () => void;
}

// Fullscreen image overlay: click anywhere or Escape to close.
export default function Lightbox({ url, alt, onClose }: LightboxProps) {
  useEffect(() => {
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        onClose();
      }
    };
    document.addEventListener('keydown', handleKey, true);
    return () => document.removeEventListener('keydown', handleKey, true);
  }, [onClose]);

  return (
    <div className="rgchat-lightbox" role="dialog" aria-modal="true" aria-label={alt ?? 'Image preview'} onClick={onClose}>
      <img src={url} alt={alt ?? ''} decoding="async" />
    </div>
  );
}
