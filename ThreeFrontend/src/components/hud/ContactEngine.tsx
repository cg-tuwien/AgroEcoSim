import { h } from 'preact';
import appstate from '../../appstate';

/**
 * Which contact engine the server runs. Three exist so they can be compared on the same request:
 * the certified engine certifies every growth increment against collisions and suspends what it
 * cannot certify; the soft engine resolves contacts in one pass per tick, bends and folds, and
 * never blocks growth - not even against obstacles, which it only pushes out of; the mixed engine
 * (the default) keeps foliage contacts soft and enforces obstacles and soil the certified way.
 * The value is the server's `ContactEngineKind` as a plain number.
 */
export function ContactEngine() {
    return <div>
        <label for="contactEngine"
            title="Mixed: leaves and stems meet each other softly (bending, folding, a little tolerated overlap) while obstacles and soil are hard - an organ never ends a step inside a wall, a tray or the ground. Certified: every growth step is certified non-penetrating against everything and suspended when it cannot be (the original engine). Soft: everything is soft, obstacles included; residual overlap is reported instead. A run resumes only under the engine that wrote its checkpoint.">
            Contact engine
        </label>
        <select name="contactEngine" value={appstate.contactEngine.value}
            onChange={e => appstate.contactEngine.value = parseInt(e.currentTarget.value)}>
            <option value={2}>Mixed: soft foliage, hard obstacles (default)</option>
            <option value={0}>Certified (everything hard)</option>
            <option value={1}>Soft (everything soft, never blocks growth)</option>
        </select>
    </div>;
}
