import { m as g, i as p, t as q, r as f, d as J, a as K, v as V, c as z, e as Y, A as Q, P as Z, B as ee, b as re } from "./chart.es.js";
/*!
 * chartjs-plugin-datalabels v2.2.0
 * https://chartjs-plugin-datalabels.netlify.app
 * (c) 2017-2022 chartjs-plugin-datalabels contributors
 * Released under the MIT license
 */
var D = function() {
  if (typeof window < "u") {
    if (window.devicePixelRatio)
      return window.devicePixelRatio;
    var e = window.screen;
    if (e)
      return (e.deviceXDPI || 1) / (e.logicalXDPI || 1);
  }
  return 1;
}(), m = {
  // @todo move this in Chart.helpers.toTextLines
  toTextLines: function(e) {
    var t = [], r;
    for (e = [].concat(e); e.length; )
      r = e.pop(), typeof r == "string" ? t.unshift.apply(t, r.split(`
`)) : Array.isArray(r) ? e.push.apply(e, r) : p(e) || t.unshift("" + r);
    return t;
  },
  // @todo move this in Chart.helpers.canvas.textSize
  // @todo cache calls of measureText if font doesn't change?!
  textSize: function(e, t, r) {
    var a = [].concat(t), i = a.length, n = e.font, o = 0, s;
    for (e.font = r.string, s = 0; s < i; ++s)
      o = Math.max(e.measureText(a[s]).width, o);
    return e.font = n, {
      height: i * r.lineHeight,
      width: o
    };
  },
  /**
   * Returns value bounded by min and max. This is equivalent to max(min, min(value, max)).
   * @todo move this method in Chart.helpers.bound
   * https://doc.qt.io/qt-5/qtglobal.html#qBound
   */
  bound: function(e, t, r) {
    return Math.max(e, Math.min(t, r));
  },
  /**
   * Returns an array of pair [value, state] where state is:
   * * -1: value is only in a0 (removed)
   * *  1: value is only in a1 (added)
   */
  arrayDiff: function(e, t) {
    var r = e.slice(), a = [], i, n, o, s;
    for (i = 0, o = t.length; i < o; ++i)
      s = t[i], n = r.indexOf(s), n === -1 ? a.push([s, 1]) : r.splice(n, 1);
    for (i = 0, o = r.length; i < o; ++i)
      a.push([r[i], -1]);
    return a;
  },
  /**
   * https://github.com/chartjs/chartjs-plugin-datalabels/issues/70
   */
  rasterize: function(e) {
    return Math.round(e * D) / D;
  }
};
function A(e, t) {
  var r = t.x, a = t.y;
  if (r === null)
    return { x: 0, y: -1 };
  if (a === null)
    return { x: 1, y: 0 };
  var i = e.x - r, n = e.y - a, o = Math.sqrt(i * i + n * n);
  return {
    x: o ? i / o : 0,
    y: o ? n / o : -1
  };
}
function ae(e, t, r, a, i) {
  switch (i) {
    case "center":
      r = a = 0;
      break;
    case "bottom":
      r = 0, a = 1;
      break;
    case "right":
      r = 1, a = 0;
      break;
    case "left":
      r = -1, a = 0;
      break;
    case "top":
      r = 0, a = -1;
      break;
    case "start":
      r = -r, a = -a;
      break;
    case "end":
      break;
    default:
      i *= Math.PI / 180, r = Math.cos(i), a = Math.sin(i);
      break;
  }
  return {
    x: e,
    y: t,
    vx: r,
    vy: a
  };
}
var te = 0, j = 1, N = 2, O = 4, F = 8;
function M(e, t, r) {
  var a = te;
  return e < r.left ? a |= j : e > r.right && (a |= N), t < r.top ? a |= F : t > r.bottom && (a |= O), a;
}
function ie(e, t) {
  for (var r = e.x0, a = e.y0, i = e.x1, n = e.y1, o = M(r, a, t), s = M(i, n, t), l, u, v; !(!(o | s) || o & s); )
    l = o || s, l & F ? (u = r + (i - r) * (t.top - a) / (n - a), v = t.top) : l & O ? (u = r + (i - r) * (t.bottom - a) / (n - a), v = t.bottom) : l & N ? (v = a + (n - a) * (t.right - r) / (i - r), u = t.right) : l & j && (v = a + (n - a) * (t.left - r) / (i - r), u = t.left), l === o ? (r = u, a = v, o = M(r, a, t)) : (i = u, n = v, s = M(i, n, t));
  return {
    x0: r,
    x1: i,
    y0: a,
    y1: n
  };
}
function P(e, t) {
  var r = t.anchor, a = e, i, n;
  return t.clamp && (a = ie(a, t.area)), r === "start" ? (i = a.x0, n = a.y0) : r === "end" ? (i = a.x1, n = a.y1) : (i = (a.x0 + a.x1) / 2, n = (a.y0 + a.y1) / 2), ae(i, n, e.vx, e.vy, t.align);
}
var E = {
  arc: function(e, t) {
    var r = (e.startAngle + e.endAngle) / 2, a = Math.cos(r), i = Math.sin(r), n = e.innerRadius, o = e.outerRadius;
    return P({
      x0: e.x + a * n,
      y0: e.y + i * n,
      x1: e.x + a * o,
      y1: e.y + i * o,
      vx: a,
      vy: i
    }, t);
  },
  point: function(e, t) {
    var r = A(e, t.origin), a = r.x * e.options.radius, i = r.y * e.options.radius;
    return P({
      x0: e.x - a,
      y0: e.y - i,
      x1: e.x + a,
      y1: e.y + i,
      vx: r.x,
      vy: r.y
    }, t);
  },
  bar: function(e, t) {
    var r = A(e, t.origin), a = e.x, i = e.y, n = 0, o = 0;
    return e.horizontal ? (a = Math.min(e.x, e.base), n = Math.abs(e.base - e.x)) : (i = Math.min(e.y, e.base), o = Math.abs(e.base - e.y)), P({
      x0: a,
      y0: i + o,
      x1: a + n,
      y1: i,
      vx: r.x,
      vy: r.y
    }, t);
  },
  fallback: function(e, t) {
    var r = A(e, t.origin);
    return P({
      x0: e.x,
      y0: e.y,
      x1: e.x + (e.width || 0),
      y1: e.y + (e.height || 0),
      vx: r.x,
      vy: r.y
    }, t);
  }
}, x = m.rasterize;
function ne(e) {
  var t = e.borderWidth || 0, r = e.padding, a = e.size.height, i = e.size.width, n = -i / 2, o = -a / 2;
  return {
    frame: {
      x: n - r.left - t,
      y: o - r.top - t,
      w: i + r.width + t * 2,
      h: a + r.height + t * 2
    },
    text: {
      x: n,
      y: o,
      w: i,
      h: a
    }
  };
}
function oe(e, t) {
  var r = t.chart.getDatasetMeta(t.datasetIndex).vScale;
  if (!r)
    return null;
  if (r.xCenter !== void 0 && r.yCenter !== void 0)
    return { x: r.xCenter, y: r.yCenter };
  var a = r.getBasePixel();
  return e.horizontal ? { x: a, y: null } : { x: null, y: a };
}
function se(e) {
  return e instanceof Q ? E.arc : e instanceof Z ? E.point : e instanceof ee ? E.bar : E.fallback;
}
function le(e, t, r, a, i, n) {
  var o = Math.PI / 2;
  if (n) {
    var s = Math.min(n, i / 2, a / 2), l = t + s, u = r + s, v = t + a - s, d = r + i - s;
    e.moveTo(t, u), l < v && u < d ? (e.arc(l, u, s, -Math.PI, -o), e.arc(v, u, s, -o, 0), e.arc(v, d, s, 0, o), e.arc(l, d, s, o, Math.PI)) : l < v ? (e.moveTo(l, r), e.arc(v, u, s, -o, o), e.arc(l, u, s, o, Math.PI + o)) : u < d ? (e.arc(l, u, s, -Math.PI, 0), e.arc(l, d, s, 0, Math.PI)) : e.arc(l, u, s, -Math.PI, Math.PI), e.closePath(), e.moveTo(t, r);
  } else
    e.rect(t, r, a, i);
}
function ue(e, t, r) {
  var a = r.backgroundColor, i = r.borderColor, n = r.borderWidth;
  !a && (!i || !n) || (e.beginPath(), le(
    e,
    x(t.x) + n / 2,
    x(t.y) + n / 2,
    x(t.w) - n,
    x(t.h) - n,
    r.borderRadius
  ), e.closePath(), a && (e.fillStyle = a, e.fill()), i && n && (e.strokeStyle = i, e.lineWidth = n, e.lineJoin = "miter", e.stroke()));
}
function ve(e, t, r) {
  var a = r.lineHeight, i = e.w, n = e.x, o = e.y + a / 2;
  return t === "center" ? n += i / 2 : (t === "end" || t === "right") && (n += i), {
    h: a,
    w: i,
    x: n,
    y: o
  };
}
function de(e, t, r) {
  var a = e.shadowBlur, i = r.stroked, n = x(r.x), o = x(r.y), s = x(r.w);
  i && e.strokeText(t, n, o, s), r.filled && (a && i && (e.shadowBlur = 0), e.fillText(t, n, o, s), a && i && (e.shadowBlur = a));
}
function fe(e, t, r, a) {
  var i = a.textAlign, n = a.color, o = !!n, s = a.font, l = t.length, u = a.textStrokeColor, v = a.textStrokeWidth, d = u && v, y;
  if (!(!l || !o && !d))
    for (r = ve(r, i, s), e.font = s.string, e.textAlign = i, e.textBaseline = "middle", e.shadowBlur = a.textShadowBlur, e.shadowColor = a.textShadowColor, o && (e.fillStyle = n), d && (e.lineJoin = "round", e.lineWidth = v, e.strokeStyle = u), y = 0, l = t.length; y < l; ++y)
      de(e, t[y], {
        stroked: d,
        filled: o,
        w: r.w,
        x: r.x,
        y: r.y + r.h * y
      });
}
var L = function(e, t, r, a) {
  var i = this;
  i._config = e, i._index = a, i._model = null, i._rects = null, i._ctx = t, i._el = r;
};
g(L.prototype, {
  /**
   * @private
   */
  _modelize: function(e, t, r, a) {
    var i = this, n = i._index, o = q(f([r.font, {}], a, n)), s = f([r.color, J.color], a, n);
    return {
      align: f([r.align, "center"], a, n),
      anchor: f([r.anchor, "center"], a, n),
      area: a.chart.chartArea,
      backgroundColor: f([r.backgroundColor, null], a, n),
      borderColor: f([r.borderColor, null], a, n),
      borderRadius: f([r.borderRadius, 0], a, n),
      borderWidth: f([r.borderWidth, 0], a, n),
      clamp: f([r.clamp, !1], a, n),
      clip: f([r.clip, !1], a, n),
      color: s,
      display: e,
      font: o,
      lines: t,
      offset: f([r.offset, 4], a, n),
      opacity: f([r.opacity, 1], a, n),
      origin: oe(i._el, a),
      padding: K(f([r.padding, 4], a, n)),
      positioner: se(i._el),
      rotation: f([r.rotation, 0], a, n) * (Math.PI / 180),
      size: m.textSize(i._ctx, t, o),
      textAlign: f([r.textAlign, "start"], a, n),
      textShadowBlur: f([r.textShadowBlur, 0], a, n),
      textShadowColor: f([r.textShadowColor, s], a, n),
      textStrokeColor: f([r.textStrokeColor, s], a, n),
      textStrokeWidth: f([r.textStrokeWidth, 0], a, n)
    };
  },
  update: function(e) {
    var t = this, r = null, a = null, i = t._index, n = t._config, o, s, l, u = f([n.display, !0], e, i);
    u && (o = e.dataset.data[i], s = V(z(n.formatter, [o, e]), o), l = p(s) ? [] : m.toTextLines(s), l.length && (r = t._modelize(u, l, n, e), a = ne(r))), t._model = r, t._rects = a;
  },
  geometry: function() {
    return this._rects ? this._rects.frame : {};
  },
  rotation: function() {
    return this._model ? this._model.rotation : 0;
  },
  visible: function() {
    return this._model && this._model.opacity;
  },
  model: function() {
    return this._model;
  },
  draw: function(e, t) {
    var r = this, a = e.ctx, i = r._model, n = r._rects, o;
    this.visible() && (a.save(), i.clip && (o = i.area, a.beginPath(), a.rect(
      o.left,
      o.top,
      o.right - o.left,
      o.bottom - o.top
    ), a.clip()), a.globalAlpha = m.bound(0, i.opacity, 1), a.translate(x(t.x), x(t.y)), a.rotate(i.rotation), ue(a, n.frame, i), fe(a, i.lines, n.text, i), a.restore());
  }
});
var he = Number.MIN_SAFE_INTEGER || -9007199254740991, ye = Number.MAX_SAFE_INTEGER || 9007199254740991;
function b(e, t, r) {
  var a = Math.cos(r), i = Math.sin(r), n = t.x, o = t.y;
  return {
    x: n + a * (e.x - n) - i * (e.y - o),
    y: o + i * (e.x - n) + a * (e.y - o)
  };
}
function W(e, t) {
  var r = ye, a = he, i = t.origin, n, o, s, l, u;
  for (n = 0; n < e.length; ++n)
    o = e[n], s = o.x - i.x, l = o.y - i.y, u = t.vx * s + t.vy * l, r = Math.min(r, u), a = Math.max(a, u);
  return {
    min: r,
    max: a
  };
}
function I(e, t) {
  var r = t.x - e.x, a = t.y - e.y, i = Math.sqrt(r * r + a * a);
  return {
    vx: (t.x - e.x) / i,
    vy: (t.y - e.y) / i,
    origin: e,
    ln: i
  };
}
var G = function() {
  this._rotation = 0, this._rect = {
    x: 0,
    y: 0,
    w: 0,
    h: 0
  };
};
g(G.prototype, {
  center: function() {
    var e = this._rect;
    return {
      x: e.x + e.w / 2,
      y: e.y + e.h / 2
    };
  },
  update: function(e, t, r) {
    this._rotation = r, this._rect = {
      x: t.x + e.x,
      y: t.y + e.y,
      w: t.w,
      h: t.h
    };
  },
  contains: function(e) {
    var t = this, r = 1, a = t._rect;
    return e = b(e, t.center(), -t._rotation), !(e.x < a.x - r || e.y < a.y - r || e.x > a.x + a.w + r * 2 || e.y > a.y + a.h + r * 2);
  },
  // Separating Axis Theorem
  // https://gamedevelopment.tutsplus.com/tutorials/collision-detection-using-the-separating-axis-theorem--gamedev-169
  intersects: function(e) {
    var t = this._points(), r = e._points(), a = [
      I(t[0], t[1]),
      I(t[0], t[3])
    ], i, n, o;
    for (this._rotation !== e._rotation && a.push(
      I(r[0], r[1]),
      I(r[0], r[3])
    ), i = 0; i < a.length; ++i)
      if (n = W(t, a[i]), o = W(r, a[i]), n.max < o.min || o.max < n.min)
        return !1;
    return !0;
  },
  /**
   * @private
   */
  _points: function() {
    var e = this, t = e._rect, r = e._rotation, a = e.center();
    return [
      b({ x: t.x, y: t.y }, a, r),
      b({ x: t.x + t.w, y: t.y }, a, r),
      b({ x: t.x + t.w, y: t.y + t.h }, a, r),
      b({ x: t.x, y: t.y + t.h }, a, r)
    ];
  }
});
function H(e, t, r) {
  var a = t.positioner(e, t), i = a.vx, n = a.vy;
  if (!i && !n)
    return { x: a.x, y: a.y };
  var o = r.w, s = r.h, l = t.rotation, u = Math.abs(o / 2 * Math.cos(l)) + Math.abs(s / 2 * Math.sin(l)), v = Math.abs(o / 2 * Math.sin(l)) + Math.abs(s / 2 * Math.cos(l)), d = 1 / Math.max(Math.abs(i), Math.abs(n));
  return u *= i * d, v *= n * d, u += t.offset * i, v += t.offset * n, {
    x: a.x + u,
    y: a.y + v
  };
}
function xe(e, t) {
  var r, a, i, n;
  for (r = e.length - 1; r >= 0; --r)
    for (i = e[r].$layout, a = r - 1; a >= 0 && i._visible; --a)
      n = e[a].$layout, n._visible && i._box.intersects(n._box) && t(i, n);
  return e;
}
function _e(e) {
  var t, r, a, i, n, o, s;
  for (t = 0, r = e.length; t < r; ++t)
    a = e[t], i = a.$layout, i._visible && (s = new Proxy(a._el, { get: (l, u) => l.getProps([u], !0)[u] }), n = a.geometry(), o = H(s, a.model(), n), i._box.update(o, n, a.rotation()));
  return xe(e, function(l, u) {
    var v = l._hidable, d = u._hidable;
    v && d || d ? u._visible = !1 : v && (l._visible = !1);
  });
}
var w = {
  prepare: function(e) {
    var t = [], r, a, i, n, o;
    for (r = 0, i = e.length; r < i; ++r)
      for (a = 0, n = e[r].length; a < n; ++a)
        o = e[r][a], t.push(o), o.$layout = {
          _box: new G(),
          _hidable: !1,
          _visible: !0,
          _set: r,
          _idx: o._index
        };
    return t.sort(function(s, l) {
      var u = s.$layout, v = l.$layout;
      return u._idx === v._idx ? v._set - u._set : v._idx - u._idx;
    }), this.update(t), t;
  },
  update: function(e) {
    var t = !1, r, a, i, n, o;
    for (r = 0, a = e.length; r < a; ++r)
      i = e[r], n = i.model(), o = i.$layout, o._hidable = n && n.display === "auto", o._visible = i.visible(), t |= o._hidable;
    t && _e(e);
  },
  lookup: function(e, t) {
    var r, a;
    for (r = e.length - 1; r >= 0; --r)
      if (a = e[r].$layout, a && a._visible && a._box.contains(t))
        return e[r];
    return null;
  },
  draw: function(e, t) {
    var r, a, i, n, o, s;
    for (r = 0, a = t.length; r < a; ++r)
      i = t[r], n = i.$layout, n._visible && (o = i.geometry(), s = H(i._el, i.model(), o), n._box.update(s, o, i.rotation()), i.draw(e, s));
  }
}, ce = function(e) {
  if (p(e))
    return null;
  var t = e, r, a, i;
  if (re(e))
    if (!p(e.label))
      t = e.label;
    else if (!p(e.r))
      t = e.r;
    else
      for (t = "", r = Object.keys(e), i = 0, a = r.length; i < a; ++i)
        t += (i !== 0 ? ", " : "") + r[i] + ": " + e[r[i]];
  return "" + t;
}, be = {
  align: "center",
  anchor: "center",
  backgroundColor: null,
  borderColor: null,
  borderRadius: 0,
  borderWidth: 0,
  clamp: !1,
  clip: !1,
  color: void 0,
  display: !0,
  font: {
    family: void 0,
    lineHeight: 1.2,
    size: void 0,
    style: void 0,
    weight: null
  },
  formatter: ce,
  labels: void 0,
  listeners: {},
  offset: 4,
  opacity: 1,
  padding: {
    top: 4,
    right: 4,
    bottom: 4,
    left: 4
  },
  rotation: 0,
  textAlign: "start",
  textStrokeColor: void 0,
  textStrokeWidth: 0,
  textShadowBlur: 0,
  textShadowColor: void 0
}, h = "$datalabels", U = "$default";
function pe(e, t) {
  var r = e.datalabels, a = {}, i = [], n, o;
  return r === !1 ? null : (r === !0 && (r = {}), t = g({}, [t, r]), n = t.labels || {}, o = Object.keys(n), delete t.labels, o.length ? o.forEach(function(s) {
    n[s] && i.push(g({}, [
      t,
      n[s],
      { _key: s }
    ]));
  }) : i.push(t), a = i.reduce(function(s, l) {
    return Y(l.listeners || {}, function(u, v) {
      s[v] = s[v] || {}, s[v][l._key || U] = u;
    }), delete l.listeners, s;
  }, {}), {
    labels: i,
    listeners: a
  });
}
function R(e, t, r, a) {
  if (t) {
    var i = r.$context, n = r.$groups, o;
    t[n._set] && (o = t[n._set][n._key], o && z(o, [i, a]) === !0 && (e[h]._dirty = !0, r.update(i)));
  }
}
function me(e, t, r, a, i) {
  var n, o;
  !r && !a || (r ? a ? r !== a && (o = n = !0) : o = !0 : n = !0, o && R(e, t.leave, r, i), n && R(e, t.enter, a, i));
}
function we(e, t) {
  var r = e[h], a = r._listeners, i, n;
  if (!(!a.enter && !a.leave)) {
    if (t.type === "mousemove")
      n = w.lookup(r._labels, t);
    else if (t.type !== "mouseout")
      return;
    i = r._hovered, r._hovered = n, me(e, a, i, n, t);
  }
}
function ge(e, t) {
  var r = e[h], a = r._listeners.click, i = a && w.lookup(r._labels, t);
  i && R(e, a, i, t);
}
var Me = {
  id: "datalabels",
  defaults: be,
  beforeInit: function(e) {
    e[h] = {
      _actives: []
    };
  },
  beforeUpdate: function(e) {
    var t = e[h];
    t._listened = !1, t._listeners = {}, t._datasets = [], t._labels = [];
  },
  afterDatasetUpdate: function(e, t, r) {
    var a = t.index, i = e[h], n = i._datasets[a] = [], o = e.isDatasetVisible(a), s = e.data.datasets[a], l = pe(s, r), u = t.meta.data || [], v = e.ctx, d, y, $, T, S, B, c, _;
    for (v.save(), d = 0, $ = u.length; d < $; ++d)
      if (c = u[d], c[h] = [], o && c && e.getDataVisibility(d) && !c.skip)
        for (y = 0, T = l.labels.length; y < T; ++y)
          S = l.labels[y], B = S._key, _ = new L(S, v, c, d), _.$groups = {
            _set: a,
            _key: B || U
          }, _.$context = {
            active: !1,
            chart: e,
            dataIndex: d,
            dataset: s,
            datasetIndex: a
          }, _.update(_.$context), c[h].push(_), n.push(_);
    v.restore(), g(i._listeners, l.listeners, {
      merger: function(k, C, X) {
        C[k] = C[k] || {}, C[k][t.index] = X[k], i._listened = !0;
      }
    });
  },
  afterUpdate: function(e) {
    e[h]._labels = w.prepare(e[h]._datasets);
  },
  // Draw labels on top of all dataset elements
  // https://github.com/chartjs/chartjs-plugin-datalabels/issues/29
  // https://github.com/chartjs/chartjs-plugin-datalabels/issues/32
  afterDatasetsDraw: function(e) {
    w.draw(e, e[h]._labels);
  },
  beforeEvent: function(e, t) {
    if (e[h]._listened) {
      var r = t.event;
      switch (r.type) {
        case "mousemove":
        case "mouseout":
          we(e, r);
          break;
        case "click":
          ge(e, r);
          break;
      }
    }
  },
  afterEvent: function(e) {
    var t = e[h], r = t._actives, a = t._actives = e.getActiveElements(), i = m.arrayDiff(r, a), n, o, s, l, u, v, d;
    for (n = 0, o = i.length; n < o; ++n)
      if (u = i[n], u[1])
        for (d = u[0].element[h] || [], s = 0, l = d.length; s < l; ++s)
          v = d[s], v.$context.active = u[1] === 1, v.update(v.$context);
    (t._dirty || i.length) && (w.update(t._labels), e.render()), delete t._dirty;
  }
};
export {
  Me as default
};
//# sourceMappingURL=chartjs-plugin-datalabels.es.js.map
