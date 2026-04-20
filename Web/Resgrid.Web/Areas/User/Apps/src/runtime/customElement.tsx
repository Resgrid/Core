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

      const module = await this.componentPromise;
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
        void this.loadComponentAsync().then(() => this.renderComponent());
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
