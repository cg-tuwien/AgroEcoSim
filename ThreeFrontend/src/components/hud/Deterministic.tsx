import { h } from "preact";
import appstate from "../../appstate";

/**
 * Reproducibility, traded against throughput.
 *
 * Not cosmetic and not a debug switch. With more than one plant the simulation ticks its
 * formations in parallel, so cross-formation messages and soil water requests accumulate in
 * whatever order the thread pool produced - and float addition is not associative. Measured: the
 * same request with the same seed is byte-identical for a single plant and diverges at the FIRST
 * timestep once there are several, with 59 of 60 stored frames differing.
 *
 * That is fine for exploring and fatal for continuing or forking a run, which is the whole reason
 * the switch exists. A child does not resume its parent - nothing here stores live world state, so
 * it is computed again from the parent's recorded request. With this on, the shared stretch comes
 * out byte-identical and the two runs are genuinely comparable; with it off the child is a second
 * sample of the same scenario and the difference between them says nothing about what was changed.
 */
export function Deterministic() {
    return <div>
        <input type="checkbox" name="deterministic" checked={appstate.deterministic}
            onChange={e => appstate.deterministic.value = e.currentTarget.checked} />
        <label for="deterministic"
            title="Runs the simulation sequentially so the same seed always gives the same result. Slower with many plants, and required if you want to continue or fork this run later and compare the result against it.">
            Reproducible run (slower)
        </label>
    </div>;
}
