import { AutocompleteRenderer, HighlightHitParams, VNode } from '../types';
export declare function createSnippetComponent({ createElement, Fragment, }: AutocompleteRenderer): {
    <THit>({ hit, attribute, tagName, }: HighlightHitParams<THit>): VNode<any>;
    __autocomplete_componentName: string;
};
