import { AutocompleteEnvironment } from '@algolia/autocomplete-core';
declare type CreateDomElementProps = Record<string, unknown> & {
    children?: Node[];
};
export declare function getCreateDomElement(environment: AutocompleteEnvironment): <KParam extends keyof HTMLElementTagNameMap>(tagName: KParam, { children, ...props }: CreateDomElementProps) => HTMLElementTagNameMap[KParam];
export {};
