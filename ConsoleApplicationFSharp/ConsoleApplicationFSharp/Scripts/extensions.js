/* global global module window */
"use strict";
// export module Extensions{
var findJsParent = () => ((typeof module !== "undefined" && module && module.exports
    || typeof module !== "undefined" && module)
    || typeof global !== "undefined" && global
    || typeof window !== "undefined" && window);
// prototypal extensions and polyfills
var addImpureExtensions = () => {
    // From https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Object/keys
    // polyfill for older browsers, which this project really doesn't need
    if (!Object.keys) {
        Object.keys = (function () {
            'use strict';
            var hasOwnProperty = Object.prototype.hasOwnProperty, hasDontEnumBug = !({ toString: null }).propertyIsEnumerable('toString'), dontEnums = [
                'toString',
                'toLocaleString',
                'valueOf',
                'hasOwnProperty',
                'isPrototypeOf',
                'propertyIsEnumerable',
                'constructor'
            ], dontEnumsLength = dontEnums.length;
            return function (obj) {
                if (typeof obj !== 'object' && (typeof obj !== 'function' || obj === null)) {
                    throw new TypeError('Object.keys called on non-object');
                }
                var result = [], prop, i;
                for (prop in obj) {
                    if (hasOwnProperty.call(obj, prop)) {
                        result.push(prop);
                    }
                }
                if (hasDontEnumBug) {
                    for (i = 0; i < dontEnumsLength; i++) {
                        if (hasOwnProperty.call(obj, dontEnums[i])) {
                            result.push(dontEnums[i]);
                        }
                    }
                }
                return result;
            };
        }());
    }
    String['trim'] = function (s) {
        if (s != null)
            return s.trim();
        return s;
    };
    // adding static and instance methods
    String['contains'] = (s, delimiter) => s != null && delimiter != null && delimiter != "" && s.indexOf(delimiter) >= 0;
    String.prototype.contains = function (delimiter) {
        return String.contains(this, delimiter);
    };
    // http://stackoverflow.com/a/1050782/57883
    Date['addHours'] = (dt, h) => {
        dt.setTime(dt.getTime() + (h * 60 * 60 * 1000));
    };
    Date.prototype.addHours = function (h) {
        Date.addHours(this, h);
    };
    if (!Date.today) {
        Date.today = function () {
            var today = new Date();
            return new Date(today.getFullYear(), today.getMonth(), today.getDate());
        };
    }
    Date.prototype.yyyyMMdd = function (separator) {
        var mm = this.getMonth() + 1;
        var dd = this.getDate().toString();
        if (separator == null)
            separator = '/';
        return [this.getFullYear(), mm < 10 ? '0' + mm : mm, dd < 10 ? '0' + dd : dd].join(separator);
    };
    Date.prototype.MMddyyyy = function (separator) {
        var mm = this.getMonth() + 1;
        var dd = this.getDate().toString();
        if (separator == null)
            separator = '/';
        return [mm < 10 ? '0' + mm : mm, dd < 10 ? '0' + dd : dd, this.getFullYear()].join(separator);
    };
    Date['to_yyyyMMdd'] = function (dateish, separator) {
        if (dateish instanceof Date) {
            return dateish.yyyyMMdd(separator);
        }
        return new Date(dateish).yyyyMMdd(separator);
    };
    Date['to_MMddyyyy'] = function (dateish, separator) {
        if (dateish instanceof Date) {
            return dateish.MMddyyyy(separator);
        }
        return new Date(dateish).MMddyyyy(separator);
    };
    // from https://stackoverflow.com/questions/6982692/html5-input-type-date-default-value-to-today
    Date.prototype.toDateInputValue = (function () {
        var local = new Date(this);
        local.setMinutes(this.getMinutes() - this.getTimezoneOffset());
        return local.toJSON().slice(0, 10);
    });
    // code from http://stackoverflow.com/a/149099/57883
    // c is the number of decimals to show
    // d is decimal separator
    // t is the thousands separator
    // this function uses redeclaration for brevity, so the implementation signature is slightly bastardized for ts
    Number.prototype.formatMoney = function (c, d, t) {
        var n = this, c = isNaN(c = Math.abs(c)) ? 2 : c, // eslint-disable-line no-redeclare
        d = d == undefined ? "." : d, // eslint-disable-line no-redeclare
        t = t == undefined ? "," : t, // eslint-disable-line no-redeclare
        s = n < 0 ? "-" : "", i = String(parseInt(n = Math.abs(Number(n) || 0).toFixed(c))), j = (j = i.length) > 3 ? j % 3 : 0;
        return s + (j ? i.substr(0, j) + t : "") + i.substr(j).replace(/(\d{3})(?=\d)/g, "$1" + t) + (c ? d + Math.abs(n - +i).toFixed(c).slice(2) : "");
    };
    // untested, going to use replace instead of remove
    // Array.prototype['remove'] = function<T>(item:T){
    // mutation : removes an item from the array, returning the removed item or undefined
    Array.prototype.remove = function (item) {
        const index = this.indexOf(item);
        if (index >= 0)
            return this.splice(index, 1)[0];
        return undefined;
    };
    // Array.prototype.replace = function<T>(item:T,replacement:T){
    Array.prototype.replace = function (item, replacement) {
        const index = this.indexOf(item);
        if (index >= 0) {
            this[index] = replacement;
            return true;
        }
        else {
            return false;
        }
    };
};
(function (exports) {
    addImpureExtensions();
    exports.findJsParent = exports.findJsParent || findJsParent;
    exports.isDifferent = exports.isDifferent = (x, y) => {
        var isX = x != null;
        var isY = y != null;
        if (!isX && !isY)
            return false;
        if (isX && !isY)
            return true;
        if (isY && !isX)
            return true;
        return x != y;
    };
    exports.todo = (msg) => {
        console.error((msg ? msg + ":" : '') + 'item stubbed as todo called');
    };
    exports.guid = () => 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
    exports.redirect = (url, app = exports) => {
        console.log('redirecting to ', url);
        if (app.location != null) {
            app.location.href = url;
        }
        else if (app.document != null) {
            app.document.location.replace(url);
        }
        else {
            console.error('unable to find redirection mechanism');
        }
    };
    exports.fetch = (url, onLoad, onFailure, method) => {
        var oReq = new XMLHttpRequest();
        oReq.addEventListener("load", onLoad);
        oReq.addEventListener("error", onFailure);
        oReq.open(method || "GET", url);
        oReq.send();
    };
    exports.inspect = (x, title, propNames) => {
        const logIt = (value) => title ? console.log(title, value) : console.log(value);
        if (propNames) {
            if (Array.isArray(propNames)) {
                propNames.map(propName => logIt(x[propName]));
            }
            else {
                logIt(x[propNames]);
            }
        }
        else
            logIt(x);
        return x;
    };
    /**
     * @param {object} prev
     * @param {object} next
     * @param {string} name - the name of the child param of both objects to compare
     */
    // advanced types https://www.typescriptlang.org/docs/handbook/advanced-types.html
    exports.getModification = (prev, next, name) => {
        const getIsChanging = (p) => prev[p] != null && p in next && next[p];
        const getIsAdding = (p) => !prev[p] && p in next && next[p];
        const getIsDeleting = (p) => prev[p] != null && p in next && !next[p];
        const result = (getIsChanging(name) ? { changeType: 'changing', next: next[name] }
            : getIsAdding(name) ? { changeType: 'adding', next: next[name] }
                : getIsDeleting(name) ? { changeType: 'deleting', next: undefined }
                    : { changeType: undefined, next: undefined });
        return result;
    };
    exports.before = (s, delimiter) => {
        if (!delimiter)
            throw Error('no delimiter provided in "' + s + "'.before(delimiter)");
        var i = s.indexOf(delimiter);
        if (i < 0)
            throw Error("delimiter('" + delimiter + "') not found in '" + s + "'");
        return s.substr(0, i);
    };
    String.prototype.before = function (delimiter) {
        return exports.before(this, delimiter);
    };
    exports.after = (s, delimiter) => {
        if (!delimiter)
            throw Error('no delimiter provided in "' + s + "'.after(delimiter)");
        var i = s.indexOf(delimiter);
        if (i < 0)
            throw Error("delimiter('" + delimiter + "') not found in '" + s + "'");
        return s.substr(s.indexOf(delimiter) + delimiter.length);
    };
    String.prototype.after = function (delimiter) {
        return exports.after(this, delimiter);
    };
    exports.mapDictionary = function (obj, f) {
        return Array.from(Object.keys(obj).map(function (k) {
            return f(k, obj[k]);
        }));
    };
    // http://stackoverflow.com/questions/728360/how-do-i-correctly-clone-a-javascript-object
    var clone = exports.clone = function (obj) {
        // Handle the 3 simple types, and null or undefined
        if (null == obj || "object" != typeof obj)
            return obj;
        // Handle Date
        if (obj instanceof Date) {
            let copy = new Date();
            copy.setTime(obj.getTime());
            return copy;
        }
        // Handle Array
        if (obj instanceof Array && Array.isArray(obj)) {
            let copy = [];
            for (var i = 0, len = obj.length; i < len; i++) {
                copy[i] = clone(obj[i]);
            }
            return copy;
        }
        // Handle Object
        if (obj instanceof Object) {
            let copy = {};
            for (var attr in obj) {
                if (obj.hasOwnProperty(attr))
                    copy[attr] = clone(obj[attr]);
            }
            return copy;
        }
        throw new Error("Unable to copy obj! Its type isn't supported.");
    };
    // type PickDelegate =
    // since the syntax doesn't seem to support object literal picks {[key]:value}, make a syntax helper
    exports.makePick = (key, value) => {
        var x = {};
        x[key] = value;
        return x;
    };
    var pickValue = () => {
        return (value) => value;
    };
    exports.makePickFromObj = pickValue;
    // add compiler error for places whose accept type is wider than what we want to be constrained to
    // https://schneidenbach.gitbooks.io/typescript-cookbook/nameof-operator.html
    exports.nameof = (name) => name;
    // const nameof = <T>(name: keyof T) => name;
    exports.flattenArray = (a, recurse) => {
        if (a == null)
            return [];
        if (Array.isArray(a)) {
            var b = a;
            var result = [].concat.apply([], b);
            if (!recurse)
                return result;
            var index;
            while ((index = result.findIndex(Array.isArray)) > -1)
                result.splice(index, 1, ...result[index]);
            return result;
        }
        return [a];
    };
    exports.isDefined = (o) => typeof o !== 'undefined' && o != null;
    /** @return {Array} */
    exports.toClassArray = (x) => {
        if (x == null)
            return [];
        if (typeof x === "string") {
            x = x.trim();
            if (x === "")
                return [];
            if (x.contains(" ")) {
                return x.split(" ").map(x => x.trim());
            }
            return [x];
        }
        if (Array.isArray(x)) {
            var result = [].concat.apply([], x.filter(x => x != null).map(exports.toClassArray));
            return result;
        }
        throw Error("should never get here");
    };
    // otherClasses: allows/adapts to inputs of type string or array
    const addClasses = exports.addClasses = (defaultClasses = [], otherClasses = []) => {
        var result = exports.toClassArray(defaultClasses).concat(exports.toClassArray(otherClasses));
        return result.filter(exports.isDefined).map(String.trim).join(' ').trim();
    };
    exports.isPositive = (x) => +x > 0;
    var getValidateClasses = exports.getValidateClasses =
        (isValid) => {
            if (isValid === undefined)
                return [];
            // returning bootstrap-classes
            switch (isValid) {
                case true:
                    return [];
                case 'success':
                    return ['has-success'];
                case false:
                case 'danger':
                case 'error':
                    return ['has-error'];
                case 'warn':
                default:
                    return ['has-warning'];
            }
        };
    exports.debounce = (function () {
        var timer = 0;
        return (function (callback, ms) {
            if (typeof (callback) !== "function")
                throw callback;
            // this method does not ever throw, or complain if passed an invalid id
            clearTimeout(timer);
            // any needed here, because NodeJs returns a non-number type
            timer = setTimeout(callback, ms); //setTimeout(callback,ms);
        });
    })();
    var debounceChange = function (callback, e, ...args) {
        if (!exports.isDefined(callback)) {
            console.info('no callback for debounceChange', e.target, typeof callback, callback);
            return;
        }
        e.persist();
        args.unshift(e.target.value);
        exports.debounce(() => callback(...args), 500);
    };
    exports.debounceChange = debounceChange;
    return exports;
})(findJsParent());
//# sourceMappingURL=extensions.js.map