/// <reference types="react" />
import { BaseItem } from '@algolia/autocomplete-core';
import { AutocompleteClassNames, AutocompleteOptions, AutocompleteRender } from './types';
export declare function getDefaultOptions<TItem extends BaseItem>(options: AutocompleteOptions<TItem>): {
    renderer: {
        classNames: AutocompleteClassNames;
        container: HTMLElement;
        getEnvironmentProps: (params: {
            props: {
                onTouchStart(event: TouchEvent): void;
                onTouchMove(event: TouchEvent): void;
                onMouseDown(event: MouseEvent): void;
            };
        } & {
            state: import("./types").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            onTouchStart(event: TouchEvent): void;
            onTouchMove(event: TouchEvent): void;
            onMouseDown(event: MouseEvent): void;
        };
        getFormProps: (params: {
            props: {
                action: "";
                noValidate: true;
                role: "search";
                onSubmit(event: Event): void;
                onReset(event: Event): void;
            };
        } & {
            state: import("./types").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            action: "";
            noValidate: true;
            role: "search";
            onSubmit(event: Event): void;
            onReset(event: Event): void;
        };
        getInputProps: (params: {
            props: {
                id: string;
                value: string;
                autoFocus: boolean;
                placeholder: string;
                autoComplete: "off" | "on";
                autoCorrect: "off" | "on";
                autoCapitalize: "off" | "on";
                enterKeyHint: "search" | "go";
                spellCheck: "false";
                maxLength: number;
                type: "search";
                'aria-autocomplete': "list" | "none" | "inline" | "both";
                'aria-activedescendant': string | undefined;
                'aria-controls': string | undefined;
                'aria-labelledby': string;
                onChange(event: Event): void;
                onKeyDown(event: KeyboardEvent): void;
                onFocus(event: Event): void;
                onBlur(): void;
                onClick(event: MouseEvent): void;
            };
            inputElement: HTMLInputElement;
        } & {
            state: import("./types").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            id: string;
            value: string;
            autoFocus: boolean;
            placeholder: string;
            autoComplete: "off" | "on";
            autoCorrect: "off" | "on";
            autoCapitalize: "off" | "on";
            enterKeyHint: "search" | "go";
            spellCheck: "false";
            maxLength: number;
            type: "search";
            'aria-autocomplete': "list" | "none" | "inline" | "both";
            'aria-activedescendant': string | undefined;
            'aria-controls': string | undefined;
            'aria-labelledby': string;
            onChange(event: Event): void;
            onKeyDown(event: KeyboardEvent): void;
            onFocus(event: Event): void;
            onBlur(): void;
            onClick(event: MouseEvent): void;
        };
        getItemProps: (params: {
            props: {
                id: string;
                role: string;
                'aria-selected': boolean;
                onMouseMove(event: MouseEvent): void;
                onMouseDown(event: MouseEvent): void;
                onClick(event: MouseEvent): void;
            };
        } & {
            state: import("./types").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            id: string;
            role: string;
            'aria-selected': boolean;
            onMouseMove(event: MouseEvent): void;
            onMouseDown(event: MouseEvent): void;
            onClick(event: MouseEvent): void;
        };
        getLabelProps: (params: {
            props: {
                htmlFor: string;
                id: string;
            };
        } & {
            state: import("./types").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            htmlFor: string;
            id: string;
        };
        getListProps: (params: {
            props: {
                role: string;
                'aria-labelledby': string;
                id: string;
            };
        } & {
            state: import("./types").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            role: string;
            'aria-labelledby': string;
            id: string;
        };
        getPanelProps: (params: {
            props: {
                onMouseDown(event: MouseEvent): void;
                onMouseLeave(): void;
            };
        } & {
            state: import("./types").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            onMouseDown(event: MouseEvent): void;
            onMouseLeave(): void;
        };
        getRootProps: (params: {
            props: {
                role: string;
                'aria-expanded': boolean;
                'aria-haspopup': boolean | "dialog" | "menu" | "true" | "false" | "grid" | "listbox" | "tree" | undefined;
                'aria-owns': string | undefined;
                'aria-labelledby': string;
            };
        } & {
            state: import("./types").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            role: string;
            'aria-expanded': boolean;
            'aria-haspopup': boolean | "dialog" | "menu" | "true" | "false" | "grid" | "listbox" | "tree" | undefined;
            'aria-owns': string | undefined;
            'aria-labelledby': string;
        };
        panelContainer: HTMLElement;
        panelPlacement: "start" | "end" | "full-width" | "input-wrapper-width";
        render: AutocompleteRender<any> | AutocompleteRender<TItem>;
        renderNoResults: AutocompleteRender<TItem> | undefined;
        renderer: {
            createElement: import("./types").Pragma;
            Fragment: any;
            render: import("./types").Render;
        };
        detachedMediaQuery: string;
        components: {
            [x: string]: (props: any) => JSX.Element;
            Highlight: <THit>({ hit, attribute, tagName, }: import("./types").HighlightHitParams<THit>) => JSX.Element;
            ReverseHighlight: <THit>({ hit, attribute, tagName, }: import("./types").HighlightHitParams<THit>) => JSX.Element;
            ReverseSnippet: <THit>({ hit, attribute, tagName, }: import("./types").HighlightHitParams<THit>) => JSX.Element;
            Snippet: <THit>({ hit, attribute, tagName, }: import("./types").HighlightHitParams<THit>) => JSX.Element;
        };
        translations: {
            detachedCancelButtonText: string;
            clearButtonTitle: string;
            submitButtonTitle: string;
        };
    };
    core: {
        id: string;
        environment: Window;
        getSources?: import("./types").GetSources<TItem> | undefined;
        initialState?: Partial<import("./types").AutocompleteState<TItem>> | undefined;
        onStateChange?(props: import("./types").OnStateChangeProps<TItem>): void;
        plugins?: import("./types").AutocompletePlugin<any, any>[] | undefined;
        debug?: boolean | undefined;
        placeholder?: string | undefined;
        autoFocus?: boolean | undefined;
        defaultActiveItemId?: number | null | undefined;
        openOnFocus?: boolean | undefined;
        stallThreshold?: number | undefined;
        navigator?: Partial<import("@algolia/autocomplete-core/dist/esm/types/AutocompleteNavigator").AutocompleteNavigator<TItem>> | undefined;
        shouldPanelOpen?(params: {
            state: import("@algolia/autocomplete-core").AutocompleteState<TItem>;
        }): boolean;
        onSubmit?(params: import("@algolia/autocomplete-core").OnSubmitParams<TItem>): void;
        onReset?(params: import("@algolia/autocomplete-core").OnResetParams<TItem>): void;
        reshape?: import("@algolia/autocomplete-core").Reshape<TItem, import("@algolia/autocomplete-core").AutocompleteState<TItem>> | undefined;
    };
};
