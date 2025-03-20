declare type WithElementProps<TProps> = TProps & Record<string, unknown> & {
    children?: Node[];
};
export declare type AutocompleteElement<TProps = {}, TElement extends HTMLOrSVGElement = HTMLOrSVGElement> = (props: WithElementProps<TProps>) => TElement;
export {};
