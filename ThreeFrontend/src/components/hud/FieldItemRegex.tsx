import { h } from "preact";
import appstate from "../../appstate";

export function FieldItemRegex() {
    return <div>
        <input type="text" name="fieldItemRegex" value={appstate.fieldItemRegex} onChange={e => {
            appstate.fieldItemRegex.value = e.currentTarget.value;
        }}/>
        <label for="fieldItemRegex">Regex for field objects</label>
    </div>;
}