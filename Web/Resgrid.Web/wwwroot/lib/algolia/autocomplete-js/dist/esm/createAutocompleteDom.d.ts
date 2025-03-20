import { AutocompleteApi as AutocompleteCoreApi, AutocompleteEnvironment, AutocompleteScopeApi, BaseItem } from '@algolia/autocomplete-core';
import { AutocompleteClassNames, AutocompleteDom, AutocompletePropGetters, AutocompleteState, AutocompleteTranslations } from './types';
declare type CreateDomProps<TItem extends BaseItem> = {
    autocomplete: AutocompleteCoreApi<TItem>;
    autocompleteScopeApi: AutocompleteScopeApi<TItem>;
    classNames: AutocompleteClassNames;
    environment: AutocompleteEnvironment;
    isDetached: boolean;
    placeholder?: string;
    propGetters: AutocompletePropGetters<TItem>;
    setIsModalOpen(value: boolean): void;
    state: AutocompleteState<TItem>;
    translations: AutocompleteTranslations;
};
export declare function createAutocompleteDom<TItem extends BaseItem>({ autocomplete, autocompleteScopeApi, classNames, environment, isDetached, placeholder, propGetters, setIsModalOpen, state, translations, }: CreateDomProps<TItem>): AutocompleteDom;
export {};
