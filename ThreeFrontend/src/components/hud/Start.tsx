import { Component, h, Fragment } from "preact";
import appstate, { PlayState } from "../../appstate";
import { batch } from "@preact/signals-core";
import { encodeTime } from "../../helpers/TimeUnits";
import { sceneStats, leafGeomIdFor } from "../../render/core/SceneStats";
//import "wired-elements"
//import Button from 'preact-material-components/Button';
//import 'preact-material-components/Button/style.css';

/**
 * Is the tab following the newest frame of a run that is still computing?
 *
 * The forward controls used to be gated on `playPointer >= history.length - 1` alone, and that
 * comparison is unstable while a run streams: the playhead follows the frames that have ARRIVED
 * while the timeline spans the frames the server has STORED, and the two chase each other. The
 * result was a "seek to end" button flickering between enabled and disabled several times a
 * second.
 *
 * There used to be a hack for this - the playback machine left the playhead one index PAST the
 * last frame, which pinned that comparison permanently true and held the button still. It also
 * left the playhead out of range, which disabled the step-back controls over a perfectly good
 * history and made the clock report a step that did not exist, so it is not the fix. This is:
 * while the tab is following the live edge, "go to the end" and "step forward" have nothing to
 * do, and saying so once is stable no matter how the two counters race.
 */
function atLiveEdge(): boolean {
    return appstate.playing.value === PlayState.ForwardWaiting;
}

export function Start(props: { inclStats: boolean }) {
    //Memoised on the scene array, so this is one walk per timestep rather than two per render -
    //at 7,700 plants the inline version was ~2.5M iterations every time the HUD redrew.
    const stats = () => sceneStats(appstate.scene.value, leafGeomIdFor);

    const time = () => {
        let playPointer = Math.max(0, Math.min(appstate.playPointer.value, appstate.history.length - 1));
        return appstate.simHoursPerTick * (appstate.history.length > 0 ? appstate.history[playPointer].t : 0);
    }

    return <p>
        <span>
            <button title="Simulate" onClick={async () => await appstate.run()}>{appstate.computing.value ? "⏹︎" : "🚀"}</button>&nbsp;
            <button title="Seek to start" onClick={async () => await appstate.play(PlayState.SeekBackward)} disabled={appstate.historyCount.value == 0 || appstate.playPointer.value <= 0}>⏮︎</button>
            <button title="Previous frame" onClick={async () => await appstate.play(PlayState.NextBackward)} disabled={appstate.playing.value != PlayState.None || appstate.historyCount.value == 0 || appstate.playPointer.value <= 0} class="stepBtn">◃</button>
            <button title="Play backward" onClick={async () => await appstate.play(PlayState.Backward)} disabled={appstate.playing.value == PlayState.Backward || appstate.historyCount.value == 0 || appstate.playPointer.value <= 0}>⏴︎</button>
            <button title="Pause" onClick={async () => await appstate.play(PlayState.None)} disabled={appstate.playing.value == PlayState.None || appstate.historyCount.value == 0}>⏸︎</button>
            <button title="Play forward" onClick={async () => await appstate.play(PlayState.Forward)} disabled={appstate.playing.value == PlayState.Forward || appstate.playing.value == PlayState.ForwardWaiting || appstate.historyCount.value == 0 || appstate.playPointer.value >= appstate.history.length - 1}>⏵︎</button>
            <button title="Next frame" onClick={async () => await appstate.play(PlayState.NextForward)} disabled={atLiveEdge() || appstate.playing.value != PlayState.None || appstate.historyCount.value == 0 || appstate.playPointer.value >= appstate.history.length - 1} class="stepBtn">▹</button>
            <button title="Seek to end" onClick={async () => await appstate.play(PlayState.SeekForward)} disabled={atLiveEdge() || appstate.historyCount.value == 0 || appstate.playPointer.value >= appstate.history.length - 1}> ⏭︎</button>
        </span>
        <span>
            <input title="Seek a frame" min={0} max={appstate.history.length - 1} type="range" name="frameslider" disabled={appstate.history.length == 0} value={appstate.playPointer} onChange={e => {
                batch(() => {
                    appstate.playPointer.value = parseInt(e.currentTarget.value);
                    appstate.playRequest.value = true;
                    appstate.playing.value = PlayState.Seek;
                });
            }}/>
            <label for="frameslider">{time()} h ({encodeTime(time())})</label>
        </span>
        {props.inclStats ? <span>
            <div>Seeds: {appstate.seeds.value.length}</div>
            <div>Obstacles: {appstate.obstacles.value.length}</div>
            <div>Objects: {stats().objects}</div>
            <div>Triangles: {stats().triangles}</div>
            <div>Backend Renderer: {appstate.renderer.value}</div>
        </span> : <></>}
    </p>;
}