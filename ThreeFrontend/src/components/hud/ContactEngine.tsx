import { h } from 'preact';
import appstate from '../../appstate';

/**
 * Which contact engine the server runs. Two exist so they can be compared on the same request:
 * the certified engine certifies every growth increment against collisions and suspends what it
 * cannot certify; the soft engine resolves contacts in one pass per tick, bends and folds, and
 * never blocks growth. The value is the server's `ContactEngineKind` as a plain number.
 */
export function ContactEngine() {
    return <div>
        <label for="contactEngine"
            title="Certified: growth is certified non-penetrating and suspended when it cannot be (the original engine). Soft: contacts bend petioles and fold leaves but never block growth or flowering; residual overlap is reported instead. A run resumes only under the engine that wrote its checkpoint.">
            Contact engine
        </label>
        <select name="contactEngine" value={appstate.contactEngine.value}
            onChange={e => appstate.contactEngine.value = parseInt(e.currentTarget.value)}>
            <option value={0}>Certified (default)</option>
            <option value={1}>Soft (never blocks growth)</option>
        </select>
    </div>;
}
