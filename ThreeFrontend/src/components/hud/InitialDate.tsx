import { h } from "preact";
import appstate from "../../appstate";
import { snapHoursPerTick, solarPosition, timeOfTimestep } from "../../helpers/Sun";

export function InitialDate() {
    const tooltip = "Calendar date the simulation starts on. It sets the sun's path, so it "
        + "changes both the available light and the direction of shadows. Until now the "
        + "backend silently used 1 February, which is a very low winter sun.";

    const date = appstate.initialDate.value;
    const requested = appstate.hoursPerTick.value;
    const snapped = snapHoursPerTick(requested);

    //Solar noon on the start date, as a quick sanity readout of what the date implies.
    const noon = solarPosition(new Date(`${date}T11:00:00Z`));

    return <div>
        <input type="date" name="initialDate" title={tooltip}
            value={date} onChange={e => appstate.initialDate.value = e.currentTarget.value} />
        <label for="initialDate" title={tooltip}>Start date</label>
        <label class="unit">(midday sun {noon.altitude.toFixed(0)}° above the horizon)</label>
        {snapped !== requested
            //The server rounds this silently, and the difference changes how far the clock
            //advances per step, so it is worth saying out loud rather than letting the sun
            //quietly disagree with the simulation.
            ? <label class="unit" style={{ color: "#a60" }}>
                &nbsp;hours per tick {requested} runs as {snapped}
            </label>
            : null}
    </div>;
}
