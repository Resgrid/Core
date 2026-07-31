import { useCallback, useEffect } from 'react';
import { getChannels, getMyPendingAcks } from './chatApi';
import { chatHub } from './chatHub';
import { setChannels, setChatAvailable, setPendingAcks } from './chatStore';
import { useChatStore } from './useChatStore';

export interface ChatBootstrap {
  available: boolean;
  loaded: boolean;
  reload: () => void;
}

// Acquires the hub, loads the channel list + pending acks, and keeps them fresh on hub events.
// Joins every channel so unread badges update live even when the conversation is not open.
export function useChatBootstrap(): ChatBootstrap {
  const reload = useCallback(() => {
    getChannels()
      .then((outcome) => {
        setChatAvailable(outcome.available);
        if (outcome.available) {
          setChannels(outcome.channels);
          for (const channel of outcome.channels) {
            if (!channel.IsArchived) {
              void chatHub.joinChannel(channel.ChatChannelId);
            }
          }
        }
      })
      .catch((error) => console.error('Failed to load chat channels.', error));
  }, []);

  useEffect(() => {
    void chatHub.acquire();
    chatHub.setChannelsRefreshHandler(reload);
    reload();
    getMyPendingAcks()
      .then((acks) => setPendingAcks(acks.map((ack) => ack.ChatMessageId)))
      .catch(() => undefined);

    return () => {
      chatHub.setChannelsRefreshHandler(null);
      chatHub.release();
    };
  }, [reload]);

  const available = useChatStore((state) => state.chatAvailable);
  const loaded = useChatStore((state) => state.channelsLoaded);
  return { available, loaded, reload };
}
