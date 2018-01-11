var __rest = (this && this.__rest) || function (s, e) {
    var t = {};
    for (var p in s) if (Object.prototype.hasOwnProperty.call(s, p) && e.indexOf(p) < 0)
        t[p] = s[p];
    if (s != null && typeof Object.getOwnPropertySymbols === "function")
        for (var i = 0, p = Object.getOwnPropertySymbols(s); i < p.length; i++) if (e.indexOf(p[i]) < 0)
            t[p[i]] = s[p[i]];
    return t;
};
/// <reference path="../node_modules/@types/react/index.d.ts" />
(function (exports) {
    // var React: typeof React = require('react');
    // const React = finder()["React"] || require("react");
    var pt = React.PropTypes;
    var addClasses = exports.addClasses;
    var debounceChange = exports.debounceChange;
    var getValidateClasses = exports.getValidateClasses;
    // figuring out the return type here is a bit of a pain and may change as the .t.ds from typings changes
    // props.x should be the item to display
    var DisplayAnything = (props) => {
        if (props.x == null)
            return "null";
        if (Array.isArray(props.x))
            return (<ul>
                {props.x.map((x, i) => <li key={i}><DisplayAnything x={x}/></li>)}
                </ul>);
        if (typeof props.x == "object") {
            return (<ul>{Object.keys(props.x).map(propName => <li key={propName}>{propName}:<DisplayAnything x={props.x[propName]}/></li>)}</ul>);
        }
        if (typeof props.x == "string")
            return (<div>{props.x}</div>);
        return null;
    };
    DisplayAnything.displayName = "DisplayAnything";
    exports.DisplayAnything = DisplayAnything;
    var ValidationFailureDisplay = (props) => {
        if (props.validationFailures == null || props.validationFailures.length == null || props.validationFailures.length < 1)
            return null;
        var vfd = {};
        var vfDetails = { 'Validation Errors': vfd };
        var vMap = props.areaMap || ((s) => s);
        // group by area
        props.validationFailures.map(vf => {
            var key = vMap(vf.area);
            if (vfd[key] == null)
                vfd[key] = [];
            var e = vfd[key];
            e.push(vf.e);
        });
        return (<span className="text-danger">
            <DisplayAnything x={vfDetails}/>
            </span>);
    };
    ValidationFailureDisplay.displayName = 'ValidationFailureDisplay';
    exports.ValidationFailureDisplay = ValidationFailureDisplay;
    exports.IsAjaxWrapperDebug = false;
    const debugAjaxWrapper = function () {
        if (exports.IsAjaxWrapperDebug) {
            console.log(arguments);
        }
    };
    // assumes the search/get starts immediately
    class AjaxWrapper extends React.Component {
        constructor(props) {
            super(props);
            var state = { data: undefined, searchError: null, loading: true, urlChanged: false };
            this.state = state;
        }
        componentWillMount() {
            debugAjaxWrapper('AjaxWrapper: componentWillMount');
            this.sendSearch(this.props.getUrl);
            debugAjaxWrapper('AjaxWrapper: sendSearch completed');
        }
        componentWillReceiveProps(nextProps) {
            debugAjaxWrapper('AjaxWrapper: componentWillReceiveProps');
            if (this.props.getUrl != nextProps.getUrl) {
                console.log('getUrl changed to ' + nextProps.getUrl);
                this.setState({ data: undefined, searchError: null, loading: true, urlChanged: true }, () => {
                    this.sendSearch(nextProps.getUrl);
                });
            }
        }
        onSearchFailed(searchText) {
            debugAjaxWrapper('AjaxWrapper: onSearchFailed');
            console.warn('ajax failed');
            this.setState({ data: undefined, searchError: 'failed to search for ' + searchText, loading: false });
        }
        // Event does not have a responseText property on the target property and EventTarget cannot be cast as XMLHttpRequestEventTarget
        // is evt really an Event?
        onSearchResults(evt) {
            console.log('onSearchResults');
            exports.evt = evt;
            var t = evt.target;
            var model = JSON.parse(t.responseText);
            debugAjaxWrapper('AjaxWrapper: onSearchResults', model, evt);
            exports.target = evt.target;
            exports.searchResults = model;
            this.setState({ data: model, loading: false });
        }
        sendSearch(url) {
            //fetch
            console.log('sendSearch', url);
            debugAjaxWrapper('AjaxWrapper :about to fetch', this.props, this.state);
            this.setState({ data: undefined, searchError: null, loading: true, urlChanged: false });
            console.log('cleared state');
            var oReq = new XMLHttpRequest();
            oReq.addEventListener("load", this.onSearchResults.bind(this));
            oReq.addEventListener("error", this.onSearchFailed.bind(this));
            oReq.open("GET", url);
            oReq.send();
        }
        render() {
            var props = this.props;
            var state = this.state;
            debugAjaxWrapper('AjaxWrapper: rendering', state);
            var rendering = props.render(state);
            debugAjaxWrapper('AjaxWrapper: rendering completed', rendering);
            return (rendering ? rendering : (<div>ajax wrapper failed to render</div>));
        }
    }
    AjaxWrapper.displayName = 'AjaxWrapper';
    AjaxWrapper.propTypes = {
        render: pt.func.isRequired,
        getUrl: pt.string.isRequired,
    };
    exports.AjaxWrapper = AjaxWrapper;
    // render the final results
    const AjaxRenderer = (props) => {
        try {
            exports.ajaxRendererProps = props;
            if (isDefined(props.searchError) || (props.loading !== true && !isDefined(props.data))) {
                debugAjaxWrapper("AjaxRenderer.Branch1", props);
                return (<div className="text-danger">{props.title} load failed</div>);
            }
            else if (props.loading === true) {
                debugAjaxWrapper("AjaxRenderer.Branch2", props);
                return (<div className="text-warning">Loading {props.title}...</div>);
            }
            else {
                debugAjaxWrapper("AjaxRenderer.Branch3", props);
                var result = props.renderData(props.data);
                if (result == null) {
                    console.error('renderer returned an invalid value', result, props.title);
                    return (<div>Error for {props.title}</div>);
                }
                return result;
            }
        }
        catch (ex) {
            console.error('ajax renderer exception', ex);
            return (<div />);
        }
    };
    AjaxRenderer.displayName = 'AjaxRenderer';
    AjaxRenderer.propTypes = {
        loading: pt.bool.isRequired,
        title: pt.string.isRequired,
        renderData: pt.func.isRequired,
        data: pt.any,
        searchError: pt.string
    };
    // curry the renderer through the wait wrapper this is the only exported component
    const Ajax = (props) => {
        var renderGiftWrapping = (state) => {
            debugAjaxWrapper("Ajax.renderGiftWrapping", state);
            var result = (<AjaxRenderer title={props.title} loading={state.loading} data={state.data} renderData={props.renderData}/>);
            debugAjaxWrapper("Ajax.result", result);
            return result;
        };
        console.log('Ajax', props.getUrl);
        return (<AjaxWrapper getUrl={props.getUrl} render={renderGiftWrapping}/>);
    };
    Ajax.displayName = 'Ajax';
    Ajax.propTypes = {
        title: pt.string.isRequired,
        getUrl: pt.string.isRequired,
        renderData: pt.func.isRequired
    };
    exports.Ajax = Ajax;
    // generic prototype for reuse
    var TextInput = exports.TextInput = (_a) => {
        var { onControlledChange, spread, onChange } = _a, props = __rest(_a, ["onControlledChange", "spread", "onChange"]);
        return (<input className={addClasses(['form-control'], props.className)} onChange={e => {
            if (onControlledChange) {
                onControlledChange(e);
            }
            if (onChange != null)
                // this works, but the definition seems to want a different Type than we are passing, so... fake it for now
                return debounceChange(onChange, e);
        }} {...props} {...spread}/>);
    };
    TextInput.displayName = 'TextInput';
    var bindAllTheThings = function (prototype) {
        Object.getOwnPropertyNames(prototype).filter(x => x != "constructor").map(x => {
            if (typeof (this[x]) === "function") {
                this[x] = this[x].bind(this);
            }
        });
    };
    exports.bindAllTheThings = bindAllTheThings;
    // https://basarat.gitbooks.io/typescript/docs/jsx/tsx.html
    // TODO: class conversion is incomplete, get initial state isn't being hooked in the constructor, etc...
    // conversion url http://www.newmediacampaigns.com/blog/refactoring-react-components-to-es6-classes
    class MoneyInput extends React.Component {
        constructor(props) {
            super(props);
            bindAllTheThings.call(this, MoneyInput.prototype);
            // Object.getOwnPropertyNames(MoneyInput.prototype).filter(x => x != "constructor").map(x => {
            //     if (typeof ((this as any)[x]) === "function"){
            //         (this as any)[x] = (this as any)[x].bind(this);
            //     }
            // });
            this.state = this.getDefaultState(props);
        }
        getDefaultState(props) {
            return { value: this.props.value };
        }
        componentWillReceiveProps(nextProps) {
            if (this.props.value !== nextProps.value) {
                var display = nextProps.value != null ? nextProps.value.formatMoney() : '';
                console.log('changing MoneyInput.state.value', this.props.value, nextProps.value, 'display', display);
                if (display != this.state.value) {
                    this.setState({ value: nextProps.value != null ? +nextProps.value : nextProps.defaultValue });
                }
            }
        }
        render() {
            // leaving this until the class conversion is completed
            var props = this.props;
            var state = this.state;
            // using the var name currentValue so as not to be confused with a controlled component's use of value
            let getClassName = (...defaultValues) => addClasses(defaultValues, getValidateClasses(props.isValid));
            var onKeyPress = (e) => {
                // ts claims this cannot be a number, but perhaps this is specifically functional
                if (e.key === '.' || !isNaN(e.key)) {
                }
                else {
                    e.preventDefault();
                    e.stopPropagation();
                }
            };
            var spread = {
                className: getClassName("money", "form-control"),
            };
            if (props.defaultValue) {
                console.warn("MoneyInput doesn't accept nor use a defaultValue, the input is controlled, so defaultValue would be thrown away");
            }
            var onChange = (...args) => {
                console.log('calling money parent', ...args);
                props.onChange(args[0]);
            };
            var onBlur = (...args) => {
                var e = args[0];
                var target = e.target;
                console.log('onBlur', ...args);
                // empty and zero don't need formatting
                if (target.value === '0')
                    return;
                if (target.value === '') {
                    console.log('onBlur wiped');
                    this.setState({ value: '' });
                    if (this.props.value != null) {
                        this.props.onChange(undefined);
                    }
                }
                else {
                    var x = (+target.value).formatMoney();
                    console.log('onBlur change from/to', target.value, x);
                    this.setState({ value: x });
                }
            };
            return (<div className={getClassName("input-group")}>
                    <span className="input-group-addon">$</span>
                    <TextInput name={props.name} 
            /* ts claims this onKeyPress handler is wrong but it seems it works just fine*/
            onKeyPress={onKeyPress} value={typeof (state.value) === 'undefined' ? '' : state.value} onControlledChange={e => this.setState({ value: e.target.value })} onChange={onChange} onBlur={onBlur.bind(this)} spread={spread}/>
            </div>);
        }
    }
    ;
    exports.MoneyInput = MoneyInput;
    MoneyInput.displayName = 'MoneyInput';
    // http://getbootstrap.com/css/#buttons
    //explicit attr doesn't appear to interfere with spread (when undefined, otherwise spread trumps the explicit)
    const Button = exports.Button =
        (_a) => {
            var { disabled, className, spread } = _a, props = __rest(_a, ["disabled", "className", "spread"]);
            return (<button type="button" disabled={disabled || typeof (props.onClick) !== 'function'} className={addClasses(["btn", "btn-default"], className)} {...props} {...spread}>
            {props.children}
        </button>);
        };
    Button.propTypes = {
        spread: React.PropTypes.object,
        disabled: React.PropTypes.bool,
        onClick: exports.PM.isFuncOrNullPropType,
    };
    Button.displayName = 'Button';
    const GlyphButton = exports.GlyphButton = (_a) => {
        var { glyphicon, children } = _a, props = __rest(_a, ["glyphicon", "children"]);
        return (<Button {...props} spread={props.spread}>
            <span className={addClasses(['glyphicon', glyphicon])} aria-hidden="true">
            </span>
            {children}
        </Button>);
    };
    GlyphButton.propTypes = {
        glyphicon: React.PropTypes.string.isRequired
    };
    GlyphButton.displayName = 'GlyphButton';
    const CheckBox = exports.CheckBox = (_a) => {
        var { label } = _a, props = __rest(_a, ["label"]);
        return (<div className="checkbox">
            <label>
                <input type="checkbox" {...props}/>
                {label}
            </label>
        </div>);
    };
    CheckBox.displayName = 'CheckBox';
    CheckBox.propTypes = {
        onChange: React.PropTypes.func.isRequired,
        checked: React.PropTypes.bool,
        title: React.PropTypes.string,
        name: React.PropTypes.any
    };
    // type OnRadioButtonChange = RadioButtonPropsOnChange | undefined | React.ChangeEventHandler<HTMLInputElement>
    let runOnRadioChange = (e, value, groupCurrentValue, onRadioButtonChange, onChange) => {
        if (onChange != null) {
            if (onRadioButtonChange == null)
                console.warn("onChange is not the correct way to use RadioButton");
            onChange(e);
        }
        if (onRadioButtonChange != null && groupCurrentValue !== value)
            onRadioButtonChange(value);
    };
    const RadioButton = exports.RadioButton = (_a) => {
        var { displayName, displayNameMap, onRadioButtonChange, onChange, checked, groupCurrentValue, type } = _a, props = __rest(_a, ["displayName", "displayNameMap", "onRadioButtonChange", "onChange", "checked", "groupCurrentValue", "type"]);
        return (<text><input type="radio" {...props} checked={groupCurrentValue === props.value} readOnly={groupCurrentValue === props.value} onChange={e => runOnRadioChange(e, props.value, groupCurrentValue, onRadioButtonChange, onChange)} //groupCurrentValue !== props.value && onRadioButtonChange ? onRadioButtonChange(props.value): null}
        /> {displayName ? displayName : displayNameMap ? displayNameMap(props.value) : props.value}</text>);
    };
    RadioButton.displayName = 'RadioButton';
    RadioButton.propTypes = {
        name: React.PropTypes.string.isRequired,
        groupCurrentValue: React.PropTypes.string,
        value: React.PropTypes.string.isRequired,
        onRadioButtonChange: React.PropTypes.func.isRequired,
        displayNameMap: React.PropTypes.func
    };
    // from https://toddmotto.com/creating-a-tabs-component-with-react/
    class Tabs extends React.Component {
        constructor(props) {
            super(props);
            this._renderTitles = this._renderTitles.bind(this);
            this.getDefaultState = this.getDefaultState.bind(this);
            this.state = this.getDefaultState(props);
        }
        getDefaultState(props) {
            return { selected: props.selected };
        }
        componentWillReceiveProps(nextProps) {
            if (nextProps.selected != this.props.selected) {
                // react uses a Pick<_,_> mess here, partial should cover any use case
                var stateMods = { selected: nextProps.selected };
                this.setState(stateMods);
            }
        }
        handleClick(index, event) {
            event.preventDefault();
            if (this.props.onTabChange)
                this.props.onTabChange(index, this.props.children && this.props.children.length > index && index >= 0 ? this.props.children[index] : undefined);
            this.setState({
                selected: index
            });
        }
        _renderTitles() {
            function labels(child, index) {
                var activeClass = this.state.selected === index ? 'activeTab' : '';
                return (<li key={index}>
                <a href="#" onClick={this.handleClick.bind(this, index)} className={activeClass}>
                    {child.props.label}
                </a>
                </li>);
            }
            return (<ul className="tabs__labels">
                {this.props.children.map(labels.bind(this))}
                </ul>);
        }
        _renderContent() {
            return (<div className="tabs__content">
                {this.props.children[this.state.selected]}
            </div>);
        }
        render() {
            return (<div className="tabs">
                {this._renderTitles()}
                {this._renderContent()}
                </div>);
        }
    }
    ;
    Tabs.defaultProps = {
        selected: 0
    };
    exports.Tabs = Tabs;
    Tabs.displayName = 'Tabs';
    class Pane extends React.Component {
        render() {
            return (<div>{this.props.children}</div>);
        }
    }
    ;
    Pane.displayName = 'Pane';
    exports.Pane = Pane;
    return exports;
})(findJsParent()); //must be "global" for node, or you can create "window" on global as a startup script
// } 
//# sourceMappingURL=components.jsx.map