import { AutocompleteOptions } from './types';
declare type GetPanelPlacementStyleParams = Pick<Required<AutocompleteOptions<any>>, 'panelPlacement' | 'environment'> & {
    container: HTMLElement;
    form: HTMLElement;
};
export declare function getPanelPlacementStyle({ panelPlacement, container, form, environment, }: GetPanelPlacementStyleParams): {
    top: number;
    left: number;
    right?: undefined;
    width?: undefined;
    maxWidth?: undefined;
} | {
    top: number;
    right: number;
    left?: undefined;
    width?: undefined;
    maxWidth?: undefined;
} | {
    top: number;
    left: number;
    right: number;
    width: string;
    maxWidth: string;
};
export {};
