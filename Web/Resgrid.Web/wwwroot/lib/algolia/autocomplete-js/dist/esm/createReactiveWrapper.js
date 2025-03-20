export function createReactiveWrapper() {
  var reactives = [];
  return {
    reactive: function reactive(value) {
      var current = value();
      var reactive = {
        _fn: value,
        _ref: {
          current: current
        },

        get value() {
          return this._ref.current;
        },

        set value(value) {
          this._ref.current = value;
        }

      };
      reactives.push(reactive);
      return reactive;
    },
    runReactives: function runReactives() {
      reactives.forEach(function (value) {
        value._ref.current = value._fn();
      });
    }
  };
}