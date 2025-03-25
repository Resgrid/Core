/// <reference types="react" />
import { AutocompleteRenderer, HighlightHitParams } from '../types';
export declare function createReverseSnippetComponent({ createElement, Fragment, }: AutocompleteRenderer): {
    <THit>({ hit, attribute, tagName, }: HighlightHitParams<THit>): JSX.Element;
    __autocomplete_componentName: string;
};
