export declare function pickBy<TValue = unknown>(obj: Record<string, TValue>, predicate: (value: {
    key: string;
    value: TValue;
}) => boolean): Record<string, TValue>;
