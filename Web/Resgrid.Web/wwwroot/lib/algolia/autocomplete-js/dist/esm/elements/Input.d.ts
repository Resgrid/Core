import { AutocompleteApi as AutocompleteCoreApi, AutocompleteEnvironment, AutocompleteScopeApi } from '@algolia/autocomplete-core';
import { AutocompletePropGetters, AutocompleteState } from '../types';
import { AutocompleteElement } from '../types/AutocompleteElement';
declare type InputProps = {
    autocompleteScopeApi: AutocompleteScopeApi<any>;
    environment: AutocompleteEnvironment;
    getInputProps: AutocompletePropGetters<any>['getInputProps'];
    getInputPropsCore: AutocompleteCoreApi<any>['getInputProps'];
    isDetached: boolean;
    state: AutocompleteState<any>;
};
export declare const Input: AutocompleteElement<InputProps, HTMLInputElement>;
export {};
