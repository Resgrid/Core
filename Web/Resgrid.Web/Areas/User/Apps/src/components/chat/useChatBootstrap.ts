import { useCallback, useEffect, useRef } from 'react';
import { getChannels, getMyPendingAcks } from './chatApi';
import { chatHub } from './chatHub';
import { setChannels, setChannelsLoadFailed, setChatAvailable, setPendingAcks, chatStore } from './chatStore';
import { seedPresenceFor } from './chatActions';
import { useChatStore } from './useChatStore';

export interface ChatBootstrap {
  available: boolean;
  loaded: boolean;
  loadFailed: boolean;
  reload: () => void;
  connect: () => void;
}

const BADGE_POLL_INTERVAL_MS = 120000;

export interface ChatBootstrapOptions {
  // True for full-page surfaces (chat page): connect the hub immediately on mount.
  // False (default) for the site-wide FAB: REST-fetch channels for badges only and
  // connect lazily via connect() the first time the panel opens.
  connectImmediately?: boolean;
}

function joinAllChannels(): void {
  for (const channel of chatStore.getState().channels) {
    if (!channel.IsArchived) {
      void chatHub.joinChannel(channel.ChatChannelId);
    }
  }
}

// Loads the channel list + pending acks and keeps them fresh on hub events. The hub itself is
// only connected when connectImmediately is set or connect() is called (lazy for the FAB).
export function useChatBootstrap(options?: ChatBootstrapOptions): ChatBootstrap {
  const connectedRef = useRef(false);

  const reload = useCallback(() => {
    setChannelsLoadFailed(false);
    getChannels()
      .then((outcome) => {
        setChatAvailable(outcome.available);
        if (outcome.available) {
          setChannels(outcome.channels);
          void seedPresenceFor(outcome.channels.map((channel) => channel.OwnerUserId));
        }
      })
      .catch((error) => {
        // Non-404 failure (network, 500, expired token): surface a retryable error state
        // instead of leaving the skeleton loader up forever.
        setChannelsLoadFailed(true);
        console.error('Failed to load chat channels.', error);
      });
  }, []);

  const connect = useCallback(() => {
    if (connectedRef.current) {
      return;
    }
    connectedRef.current = true;
    chatHub
      .ensureConnected()
      .then(joinAllChannels)
      .catch(() => {
        // Offline at open time; reconnect policy + delta sync recover automatically.
      });
  }, []);

  useEffect(() => {
    if (options?.connectImmediately) {
      connect();
    }
    const unsubscribe = chatHub.subscribeChannelsRefresh(reload);
    reload();
    getMyPendingAcks()
      .then((acks) => setPendingAcks(acks.map((ack) => ack.ChatMessageId)))
      .catch(() => undefined);

    // Badge freshness while the hub is disconnected (panel never opened on this page).
    // Skip once the server said chat is unavailable (404) — no point re-asking every poll.
    const poll = setInterval(() => {
      const state = chatStore.getState();
      if (state.chatAvailable && state.connectionStatus !== 'connected') {
        reload();
      }
    }, BADGE_POLL_INTERVAL_MS);

    return () => {
      clearInterval(poll);
      unsubscribe();
    };
  }, [reload, connect, options?.connectImmediately]);

  const available = useChatStore((state) => state.chatAvailable);
  const loaded = useChatStore((state) => state.channelsLoaded);
  const loadFailed = useChatStore((state) => state.channelsLoadFailed);
  return { available, loaded, loadFailed, reload, connect };
}
