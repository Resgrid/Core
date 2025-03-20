declare type ReactiveValue<TValue> = () => TValue;
export declare type Reactive<TValue> = {
    value: TValue;
    /**
     * @private
     */
    _fn: ReactiveValue<TValue>;
    /**
     * @private
     */
    _ref: {
        current: TValue;
    };
};
export declare function createReactiveWrapper(): {
    reactive<TValue>(value: ReactiveValue<TValue>): Reactive<TValue>;
    runReactives(): void;
};
export {};
