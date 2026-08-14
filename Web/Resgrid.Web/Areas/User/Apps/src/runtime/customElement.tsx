import { createElement } from 'react';
import type { ComponentType } from 'react';
import { createRoot, type Root } from 'react-dom/client';

export type PropKind = 'string' | 'boolean' | 'number' | 'json';

export interface PropDefinition<TValue = unknown> {
  attribute: string;
  property: string;
  type: PropKind;
  defaultValue?: TValue;
}

export interface HostedReactElementProps {
  hostElement: HTMLElement;
}

type ComponentLoader<TProps> = () => Promise<{ default: ComponentType<TProps> }>;

// A deploy rotates the hashed chunk filenames, so a page loaded before the deploy 404s
// when it lazily imports an element chunk. One forced reload picks up the fresh HTML and
// hashes; the session flag stops a reload loop when the failure is anything else.
const CHUNK_RELOAD_FLAG = 'rg-elements-chunk-reload';

function recoverFromChunkLoadFailure(tagName: string, error: unknown): void {
  try {
    if (!sessionStorage.getItem(CHUNK_RELOAD_FLAG)) {
      sessionStorage.setItem(CHUNK_RELOAD_FLAG, '1');
      window.location.reload();
      return;
    }
  } catch {
    // sessionStorage unavailable (private browsing) — fall through to the console
  }

  console.error(`Failed to load the component for <${tagName}>`, error);
}

function clearChunkReloadFlag(): void {
  try {
    sessionStorage.removeItem(CHUNK_RELOAD_FLAG);
  } catch {
    // ignore
  }
}

function parseAttributeValue(rawValue: string | null, definition: PropDefinition): unknown {
  if (rawValue === null) {
    return definition.defaultValue;
  }

  switch (definition.type) {
    case 'boolean':
      return rawValue === '' || rawValue === 'true' || rawValue === '1';
    case 'number': {
      const parsedValue = Number(rawValue);
      return Number.isFinite(parsedValue) ? parsedValue : definition.defaultValue;
    }
    case 'json':
      try {
        return JSON.parse(rawValue);
      } catch {
        return definition.defaultValue;
      }
    case 'string':
    default:
      return rawValue;
  }
}

export function defineReactElement<TProps extends object>(
  tagName: string,
  loader: ComponentLoader<TProps & HostedReactElementProps>,
  propDefinitions: readonly PropDefinition[],
): void {
  if (customElements.get(tagName)) {
    return;
  }

  class HostedReactElement extends HTMLElement {
    public static get observedAttributes(): string[] {
      return propDefinitions.map((definition) => definition.attribute);
    }

    private root: Root | null = null;
    private mountPoint: HTMLDivElement | null = null;
    private component: ComponentType<TProps & HostedReactElementProps> | null = null;
    private componentPromise: Promise<{ default: ComponentType<TProps & HostedReactElementProps> }> | null = null;
    private propertyValues = new Map<string, unknown>();

    public connectedCallback(): void {
      if (!this.mountPoint) {
        this.mountPoint = document.createElement('div');
        this.mountPoint.className = 'rg-element-root';
        this.replaceChildren(this.mountPoint);
      }

      if (!this.root) {
        this.root = createRoot(this.mountPoint);
      }

      this.renderComponent();
    }

    public attributeChangedCallback(): void {
      this.renderComponent();
    }

    public disconnectedCallback(): void {
      this.root?.unmount();
      this.root = null;
      this.mountPoint = null;
    }

    private async loadComponentAsync(): Promise<ComponentType<TProps & HostedReactElementProps>> {
      if (!this.componentPromise) {
        this.componentPromise = loader();
      }

      let module: { default: ComponentType<TProps & HostedReactElementProps> };
      try {
        module = await this.componentPromise;
      } catch (error) {
        // Drop the failed promise so a later render can retry instead of re-awaiting the failure.
        this.componentPromise = null;
        throw error;
      }

      clearChunkReloadFlag();
      this.component = module.default;
      return this.component;
    }

    private buildProps(): TProps & HostedReactElementProps {
      const props: Record<string, unknown> = {
        hostElement: this,
      };

      for (const definition of propDefinitions) {
        props[definition.property] = parseAttributeValue(this.getAttribute(definition.attribute), definition);
      }

      for (const [key, value] of this.propertyValues.entries()) {
        props[key] = value;
      }

      return props as TProps & HostedReactElementProps;
    }

    private renderComponent(): void {
      if (!this.isConnected || !this.root) {
        return;
      }

      if (!this.component) {
        void this.loadComponentAsync()
          .then(() => this.renderComponent())
          .catch((error: unknown) => recoverFromChunkLoadFailure(tagName, error));
        return;
      }

      this.root.render(createElement(this.component, this.buildProps()));
    }

    public setPropertyValue(propertyName: string, propertyValue: unknown): void {
      if (typeof propertyValue === 'undefined') {
        this.propertyValues.delete(propertyName);
      } else {
        this.propertyValues.set(propertyName, propertyValue);
      }

      this.renderComponent();
    }

    public getPropertyValue(propertyName: string): unknown {
      return this.propertyValues.get(propertyName);
    }
  }

  for (const definition of propDefinitions) {
    Object.defineProperty(HostedReactElement.prototype, definition.property, {
      configurable: true,
      enumerable: true,
      get(this: HostedReactElement) {
        return this.getPropertyValue(definition.property);
      },
      set(this: HostedReactElement, value: unknown) {
        this.setPropertyValue(definition.property, value);
      },
    });
  }

  customElements.define(tagName, HostedReactElement);
}
