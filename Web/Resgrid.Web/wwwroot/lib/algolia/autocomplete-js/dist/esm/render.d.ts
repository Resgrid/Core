/** @jsxRuntime classic */
/** @jsx renderer.createElement */
import { AutocompleteApi as AutocompleteCoreApi, AutocompleteScopeApi, BaseItem } from '@algolia/autocomplete-core';
import { AutocompleteClassNames, AutocompleteComponents, AutocompleteDom, AutocompletePropGetters, AutocompleteRender, AutocompleteRenderer, AutocompleteState, HTMLTemplate } from './types';
declare type RenderProps<TItem extends BaseItem> = {
    autocomplete: AutocompleteCoreApi<TItem>;
    autocompleteScopeApi: AutocompleteScopeApi<TItem>;
    classNames: AutocompleteClassNames;
    components: AutocompleteComponents;
    html: HTMLTemplate;
    dom: AutocompleteDom;
    panelContainer: HTMLElement;
    propGetters: AutocompletePropGetters<TItem>;
    state: AutocompleteState<TItem>;
    renderer: Required<AutocompleteRenderer>;
};
export declare function renderSearchBox<TItem extends BaseItem>({ autocomplete, autocompleteScopeApi, dom, propGetters, state, }: RenderProps<TItem>): void;
export declare function renderPanel<TItem extends BaseItem>(render: AutocompleteRender<TItem>, { autocomplete, autocompleteScopeApi, classNames, html, dom, panelContainer, propGetters, state, components, renderer, }: RenderProps<TItem>): void;
export {};
