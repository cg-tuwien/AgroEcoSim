import { h, Fragment } from "preact";
import appstate from "../../appstate";
import { Signal, useSignal } from "@preact/signals";
import { JSXInternal } from "preact/src/jsx";
import { conflictStyle } from "./Species";

interface IVariance {
    step?: number,
    signal: Signal<number>,
}

type IVarianceWithChange = IVariance & { onChange: h.JSX.GenericEventHandler<HTMLInputElement> }

export function SpeciesFloatField(props: {min?: number, max?: number, step: number, speciesIndex: number, tooltip?: string, signal: Signal<number>, label: string, precision?: number, variance?: IVariance})
{
    if (props.variance && props.variance.step === undefined)
        props.variance.step = 0.1;

    const v = props.variance ? {...props.variance, onChange: e => props.variance!.signal.value = parseFloat(e.currentTarget.value)} : undefined;

    return SpeciesNumericField({...props, onChange: e => props.signal.value = parseFloat(e.currentTarget.value), variance: v})
}

export function SpeciesIntField(props: {min?: number, max?: number, step?: number, speciesIndex: number, tooltip?: string, signal: Signal<number>, label: string, precision?: number, variance?: IVariance})
{
    props.step = Math.max(1, Math.round(props.step ?? 1));

    props.precision = 0;
    if (props.variance && props.variance.step === undefined)
        props.variance.step = 1;

    const v = props.variance ? {...props.variance, onChange: e => props.variance!.signal.value = parseInt(e.currentTarget.value)} : undefined;

    return SpeciesNumericField({...props, step: props.step, onChange: e => props.signal.value = parseInt(e.currentTarget.value), variance: v})
}

function SpeciesNumericField(props: {min?: number, max?: number, step: number, speciesIndex: number, tooltip?: string, signal: Signal<number>, label: string, precision?: number, onChange: h.JSX.GenericEventHandler<HTMLInputElement>, variance?: IVarianceWithChange})
{
    if (props.min === undefined)
        props.min = 0;

    if (props.precision === undefined)
        props.precision = 4;

    if (props.max === undefined)
        props.max = Number.MAX_VALUE;

    props.tooltip ??= "";
    const name = props.label.replaceAll(' ', '_').toLocaleLowerCase().replace(/[^a-z0-9_]/gi, '');

    return <>
            <div>
                <input min={props.min} max={props.max} step={props.step} type="number" name={`${name}-${props.speciesIndex}`} title={props.tooltip} value={formatNumber(props.signal.value, props.precision)} onChange={props.onChange}  />
                <label for={`${name}-${props.speciesIndex}`} title={props.tooltip}>{props.label}</label>
                {props.variance ? <>
                    <span>var:&nbsp;</span>
                    <input min={0} step={props.variance.step} type="number" name={`${name}Var-${props.speciesIndex}`} value={formatNumber(props.variance.signal.value, props.precision)} onChange={props.variance.onChange} />
                </> : <></>}
            </div>
        </>
}

function formatNumber(value: number, precision: number) {
    return Number.isFinite(value) ? +value.toFixed(precision) : 0;
}

function readFloat(value: string) {
    const n = parseFloat(value);
    return Number.isFinite(n) ? n : 0;
}

function readInt(value: string) {
    const n = parseInt(value, 10);
    return Number.isFinite(n) ? n : 0;
}

export function SpeciesBehaviorField(props: {speciesIndex: number, signal: Signal<number>, options: Signal<string[]>}) {
    const name = `behavior-${props.speciesIndex}`;
    return <>
            <div>
                <select name={name} onChange={e => props.signal.value = (e.target as HTMLSelectElement).selectedIndex}>
                    {props.options.value.map((x, i) => <option selected={props.signal.value == i}>{x}</option>)}
                </select>
                <label for={name}>Behavior</label>
            </div>
        </>;
}

export function SpeciesNameFields(props: {speciesIndex: number, nameSignal: Signal<string>, akaSignal: Signal<string>, linksCount: number}) {
    const nameConflict = useSignal(appstate.species.value.some((s, i) => i !== props.speciesIndex && s.name.value == n));
    const n = props.nameSignal.value;
    return <>
        <div>
            <input type="text" name={`name-${props.speciesIndex}`} value={props.nameSignal.value} title={"Name of the species"} style={nameConflict.value ? conflictStyle : undefined} class="speciesNameInput" onChange={e => {
                const name = e.currentTarget.value;
                if (appstate.species.value.some((s, i) => i !== props.speciesIndex && s.name.value == name))
                {
                    nameConflict.value = true;
                    e.currentTarget.value = props.nameSignal.value;
                    setTimeout(() => nameConflict.value = false, 2000);
                }
                else
                {
                    props.nameSignal.value = name;
                    nameConflict.value = false;
                }
            }} />
            <label for={`name-${props.speciesIndex}`}>Name {(nameConflict.value ? <span style={{color: "#ee2211"}}>conflict!</span> : <></>)}</label>
            <button style={{float: "right"}} onClick={() => appstate.removeSpeciesAt(props.speciesIndex)} disabled={props.linksCount > 0 || appstate.species.value.length <= 1}>🗙</button>
            <span style={{float: "right", marginRight: "0.5em"}}>🔗 {props.linksCount}</span>
            <label for={`aka-${props.speciesIndex}`} title={"Colloquial name"}>aka</label>
            <input type="text" name={`aka-${props.speciesIndex}`} value={props.akaSignal.value ?? ''} title={"Colloquial name"} class="speciesAkaInput" onChange={e => props.akaSignal.value = e.currentTarget.value} />
        </div>
    </>;
}