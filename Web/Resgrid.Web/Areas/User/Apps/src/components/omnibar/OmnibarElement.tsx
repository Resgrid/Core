import { useEffect, useMemo, useRef, useState } from 'react';
import { dispatchElementEvent } from '../../runtime/events';
import './omnibar.css';

export interface OmnibarItem {
  id?: string | number;
  label: string;
  description?: string;
  url?: string;
  keywords?: string[];
  [key: string]: unknown;
}

export interface OmnibarElementProps {
  title?: string;
  placeholder?: string;
  items?: OmnibarItem[];
  showCount?: boolean;
  autoFocus?: boolean;
  emptyText?: string;
  maxItems?: number;
  hostElement?: HTMLElement;
}

function normalizeItems(items: unknown): OmnibarItem[] {
  if (!Array.isArray(items)) {
    return [];
  }

  return items.reduce<OmnibarItem[]>((result, item) => {
      if (!item || typeof item !== 'object') {
        return result;
      }

      const candidate = item as Record<string, unknown>;
      const label =
        (typeof candidate.label === 'string' && candidate.label) ||
        (typeof candidate.title === 'string' && candidate.title) ||
        (typeof candidate.name === 'string' && candidate.name) ||
        '';

      if (label.length === 0) {
        return result;
      }

      const description =
        typeof candidate.description === 'string' ? candidate.description : undefined;
      const url = typeof candidate.url === 'string' ? candidate.url : undefined;
      const keywords = Array.isArray(candidate.keywords)
        ? candidate.keywords.filter((keyword): keyword is string => typeof keyword === 'string')
        : undefined;

      result.push({
        ...(candidate as OmnibarItem),
        label,
        description,
        url,
        keywords,
      });

      return result;
    }, []);
}

export default function OmnibarElement({
  title = '',
  placeholder = 'Search',
  items,
  showCount = true,
  autoFocus = false,
  emptyText = 'No matching results',
  maxItems = 8,
  hostElement,
}: OmnibarElementProps) {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [query, setQuery] = useState('');
  const [activeIndex, setActiveIndex] = useState(0);

  const normalizedItems = useMemo(() => normalizeItems(items), [items]);

  const filteredItems = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    const limitedSize = Math.max(1, maxItems);

    const matchingItems = normalizedItems.filter((item) => {
      if (normalizedQuery.length === 0) {
        return true;
      }

      const haystack = [
        item.label,
        item.description,
        ...(item.keywords ?? []),
      ]
        .filter((value): value is string => typeof value === 'string')
        .join(' ')
        .toLowerCase();

      return haystack.includes(normalizedQuery);
    });

    return matchingItems.slice(0, limitedSize);
  }, [maxItems, normalizedItems, query]);

  useEffect(() => {
    if (autoFocus) {
      inputRef.current?.focus();
    }
  }, [autoFocus]);

  useEffect(() => {
    setActiveIndex((currentIndex) => {
      if (filteredItems.length === 0) {
        return 0;
      }

      return Math.min(currentIndex, filteredItems.length - 1);
    });
  }, [filteredItems]);

  useEffect(() => {
    if (hostElement) {
      dispatchElementEvent(hostElement, 'querychange', {
        query,
        items: filteredItems,
      });
    }
  }, [filteredItems, hostElement, query]);

  const selectItem = (item: OmnibarItem) => {
    if (hostElement) {
      dispatchElementEvent(hostElement, 'itemselect', item);
    }

    if (item.url) {
      window.location.assign(item.url);
    }
  };

  const submitQuery = () => {
    if (hostElement) {
      dispatchElementEvent(hostElement, 'submit', {
        query,
      });
    }
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex((currentIndex) =>
        filteredItems.length === 0 ? 0 : Math.min(currentIndex + 1, filteredItems.length - 1),
      );
      return;
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex((currentIndex) => Math.max(currentIndex - 1, 0));
      return;
    }

    if (event.key === 'Enter') {
      event.preventDefault();

      const selectedItem = filteredItems[activeIndex];

      if (selectedItem) {
        selectItem(selectedItem);
      } else {
        submitQuery();
      }
    }
  };

  return (
    <div className="rg-omnibar">
      <div className="rg-omnibar__header">
        <div className="rg-omnibar__title">{title || 'Omnibar'}</div>

        {showCount && (
          <div className="rg-omnibar__count">
            {filteredItems.length} result{filteredItems.length === 1 ? '' : 's'}
          </div>
        )}
      </div>

      <input
        ref={inputRef}
        className="rg-omnibar__input"
        type="search"
        value={query}
        placeholder={placeholder}
        onChange={(event) => setQuery(event.target.value)}
        onKeyDown={handleKeyDown}
      />

      {filteredItems.length === 0 ? (
        <div className="rg-omnibar__empty">{emptyText}</div>
      ) : (
        <div className="rg-omnibar__results">
          {filteredItems.map((item, index) => (
            <button
              key={`${item.id ?? item.label}-${index}`}
              type="button"
              className={`rg-omnibar__item${index === activeIndex ? ' rg-omnibar__item--active' : ''}`}
              onMouseEnter={() => setActiveIndex(index)}
              onClick={() => selectItem(item)}
            >
              <span className="rg-omnibar__item-label">{item.label}</span>
              {item.description && (
                <span className="rg-omnibar__item-description">{item.description}</span>
              )}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
