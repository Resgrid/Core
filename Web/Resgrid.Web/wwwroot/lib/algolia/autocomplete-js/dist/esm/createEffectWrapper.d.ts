declare type Effect = () => void;
declare type EffectFn = () => Effect;
declare type EffectWrapper = {
    runEffect(fn: EffectFn): void;
    cleanupEffects(): void;
    runEffects(): void;
};
export declare function createEffectWrapper(): EffectWrapper;
export {};
