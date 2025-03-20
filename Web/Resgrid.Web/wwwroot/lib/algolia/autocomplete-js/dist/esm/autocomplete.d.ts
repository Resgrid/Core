import { BaseItem } from '@algolia/autocomplete-core';
import { AutocompleteApi, AutocompleteOptions } from './types';
export declare function autocomplete<TItem extends BaseItem>(options: AutocompleteOptions<TItem>): AutocompleteApi<TItem>;
