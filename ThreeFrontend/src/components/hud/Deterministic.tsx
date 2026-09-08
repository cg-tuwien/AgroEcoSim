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
 * That is fine for exploring and fatal for branching, which is the whole reason the switch
 * exists: a scenario branched from a stored checkpoint can only be compared against its parent if
 * the parent could be reproduced in the first place.
 */
export function Deterministic() {
    return <div>
        <input type="checkbox" name="deterministic" checked={appstate.deterministic}
            onChange={e => appstate.deterministic.value = e.currentTarget.checked} />
        <label for="deterministic"
            title="Runs the simulation sequentially so the same seed always gives the same result. Slower with many plants, and required if you want to branch a scenario and compare it against the run it came from.">
            Reproducible run (slower)
        </label>
    </div>;
}
