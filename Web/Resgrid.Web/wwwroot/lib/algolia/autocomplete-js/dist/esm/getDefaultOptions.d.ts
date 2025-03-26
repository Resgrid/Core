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
            state: import("@algolia/autocomplete-shared").AutocompleteState<TItem>;
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
            state: import("@algolia/autocomplete-shared").AutocompleteState<TItem>;
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
                autoComplete: "on" | "off";
                autoCorrect: "on" | "off";
                autoCapitalize: "on" | "off";
                enterKeyHint: import("@algolia/autocomplete-core").AutocompleteEnterKeyHint;
                spellCheck: "false";
                maxLength: number;
                type: "search";
                'aria-autocomplete': "list" | "none" | "inline" | "both";
                'aria-activedescendant': string | undefined;
                'aria-controls': string | undefined;
                'aria-labelledby': string;
                onChange(event: Event): void;
                onCompositionEnd(event: Event): void;
                onKeyDown(event: KeyboardEvent): void;
                onFocus(event: Event): void;
                onBlur(): void;
                onClick(event: MouseEvent): void;
            };
            inputElement: HTMLInputElement;
        } & {
            state: import("@algolia/autocomplete-shared").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            id: string;
            value: string;
            autoFocus: boolean;
            placeholder: string;
            autoComplete: "on" | "off";
            autoCorrect: "on" | "off";
            autoCapitalize: "on" | "off";
            enterKeyHint: import("@algolia/autocomplete-core").AutocompleteEnterKeyHint;
            spellCheck: "false";
            maxLength: number;
            type: "search";
            'aria-autocomplete': "list" | "none" | "inline" | "both";
            'aria-activedescendant': string | undefined;
            'aria-controls': string | undefined;
            'aria-labelledby': string;
            onChange(event: Event): void;
            onCompositionEnd(event: Event): void;
            onKeyDown(event: KeyboardEvent): void;
            onFocus(event: Event): void;
            onBlur(): void;
            onClick(event: MouseEvent): void;
        };
        getItemProps: (params: {
            props: {
                id: string;
                role: "option";
                'aria-selected': boolean;
                onMouseMove(event: MouseEvent): void;
                onMouseDown(event: MouseEvent): void;
                onClick(event: MouseEvent): void;
            };
        } & {
            state: import("@algolia/autocomplete-shared").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            id: string;
            role: "option";
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
            state: import("@algolia/autocomplete-shared").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            htmlFor: string;
            id: string;
        };
        getListProps: (params: {
            props: {
                role: "listbox";
                'aria-labelledby': string;
                id: string;
            };
        } & {
            state: import("@algolia/autocomplete-shared").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            role: "listbox";
            'aria-labelledby': string;
            id: string;
        };
        getPanelProps: (params: {
            props: {
                onMouseDown(event: MouseEvent): void;
                onMouseLeave(): void;
            };
        } & {
            state: import("@algolia/autocomplete-shared").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            onMouseDown(event: MouseEvent): void;
            onMouseLeave(): void;
        };
        getRootProps: (params: {
            props: {
                role: "combobox";
                'aria-expanded': boolean;
                'aria-haspopup': boolean | "dialog" | "menu" | "true" | "false" | "grid" | "listbox" | "tree" | undefined;
                'aria-controls': string | undefined;
                'aria-labelledby': string;
            };
        } & {
            state: import("@algolia/autocomplete-shared").AutocompleteState<TItem>;
        } & import("@algolia/autocomplete-core").AutocompleteScopeApi<TItem>) => {
            role: "combobox";
            'aria-expanded': boolean;
            'aria-haspopup': boolean | "dialog" | "menu" | "true" | "false" | "grid" | "listbox" | "tree" | undefined;
            'aria-controls': string | undefined;
            'aria-labelledby': string;
        };
        panelContainer: HTMLElement;
        panelPlacement: "start" | "end" | "full-width" | "input-wrapper-width";
        render: AutocompleteRender<any> | AutocompleteRender<TItem>;
        renderNoResults: AutocompleteRender<TItem> | undefined;
        renderer: {
            createElement: import("@algolia/autocomplete-shared").Pragma;
            Fragment: any;
            render: import("@algolia/autocomplete-shared").Render;
        };
        detachedMediaQuery: string;
        components: {
            [x: string]: (props: any) => import("@algolia/autocomplete-shared").VNode<any>;
            Highlight: <THit>({ hit, attribute, tagName, }: import("@algolia/autocomplete-shared").HighlightHitParams<THit>) => import("@algolia/autocomplete-shared").VNode<any>;
            ReverseHighlight: <THit>({ hit, attribute, tagName, }: import("@algolia/autocomplete-shared").HighlightHitParams<THit>) => import("@algolia/autocomplete-shared").VNode<any>;
            ReverseSnippet: <THit>({ hit, attribute, tagName, }: import("@algolia/autocomplete-shared").HighlightHitParams<THit>) => import("@algolia/autocomplete-shared").VNode<any>;
            Snippet: <THit>({ hit, attribute, tagName, }: import("@algolia/autocomplete-shared").HighlightHitParams<THit>) => import("@algolia/autocomplete-shared").VNode<any>;
        };
        translations: {
            detachedCancelButtonText: string;
            clearButtonTitle: string;
            submitButtonTitle: string;
            detachedSearchButtonTitle: string;
        };
    };
    core: {
        id: string;
        environment: Window;
        insights?: boolean | import("@algolia/autocomplete-plugin-algolia-insights").CreateAlgoliaInsightsPluginParams | undefined;
        getSources?: import("@algolia/autocomplete-shared").GetSources<TItem> | undefined;
        initialState?: Partial<import("@algolia/autocomplete-shared").AutocompleteState<TItem>> | undefined;
        onStateChange?(props: import("@algolia/autocomplete-shared").OnStateChangeProps<TItem>): void;
        plugins?: import("@algolia/autocomplete-shared").AutocompletePlugin<any, any>[] | undefined;
        debug?: boolean | undefined;
        enterKeyHint?: import("@algolia/autocomplete-core").AutocompleteEnterKeyHint | undefined;
        ignoreCompositionEvents?: boolean | undefined;
        placeholder?: string | undefined;
        autoFocus?: boolean | undefined;
        defaultActiveItemId?: number | null | undefined;
        openOnFocus?: boolean | undefined;
        stallThreshold?: number | undefined;
        navigator?: Partial<import("@algolia/autocomplete-shared/dist/esm/core/AutocompleteNavigator").AutocompleteNavigator<TItem>> | undefined;
        shouldPanelOpen?(params: {
            state: import("@algolia/autocomplete-core").AutocompleteState<TItem>;
        }): boolean;
        onSubmit?(params: import("@algolia/autocomplete-core").OnSubmitParams<TItem>): void;
        onReset?(params: import("@algolia/autocomplete-core").OnResetParams<TItem>): void;
        reshape?: import("@algolia/autocomplete-core").Reshape<TItem, import("@algolia/autocomplete-core").AutocompleteState<TItem>> | undefined;
    };
};
