import { h } from "preact";
import appstate from "../../appstate";

/**
 * Forces the sequential tick. A diagnostic, no longer a trade.
 *
 * The parallel tick used to be irreproducible - formations ticked concurrently and their soil water
 * requests reached the soil in thread-pool order, so the same request with the same seed diverged
 * at the first timestep once there were several plants - and this switch bought reproducibility
 * at the price of throughput, for continuing or forking a run. The requests are now buffered per
 * plant and processed in formation order, and the parallel tick is byte-identical to the
 * sequential one (measured at 100 and 400 plants). Every run is reproducible; this only slows it
 * down, and exists so a divergence between the two paths can still be investigated.
 */
export function Deterministic() {
    return <div>
        <input type="checkbox" name="deterministic" checked={appstate.deterministic}
            onChange={e => appstate.deterministic.value = e.currentTarget.checked} />
        <label for="deterministic"
            title="Runs the formations one after another instead of in parallel. Not needed for reproducibility any more - the parallel tick gives the same result - so leave it off unless you are investigating a difference between the two.">
            Sequential tick (diagnostic, slower)
        </label>
    </div>;
}
