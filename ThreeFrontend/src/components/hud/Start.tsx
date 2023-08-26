import { Component, h, Fragment } from "preact";
import appstate, { PlayState } from "../../appstate";
import { batch } from "@preact/signals-core";
import { encodeTime } from "../../helpers/TimeUnits";
//import "wired-elements"
//import Button from 'preact-material-components/Button';
//import 'preact-material-components/Button/style.css';

const tris = [0, 12, 32, 0, 128, 0, 0, 0, 2];
export function Start(props: { inclStats: boolean }) {
    const time = () => {
        let playPointer = Math.max(0, Math.min(appstate.playPointer.value, appstate.history.length - 1));
        return appstate.simHoursPerTick * (appstate.history.length > 0 ? appstate.history[playPointer].t : 0);
    }

    return <p>
        <span>
            <button title="Simulate" onClick={async () => await appstate.run()}>{appstate.computing.value ? "⏹︎" : "🚀"}</button>&nbsp;
            <button title="Seek to start" onClick={async () => await appstate.play(PlayState.SeekBackward)} disabled={appstate.historySize.value == 0 || appstate.playPointer.value <= 0}>⏮︎</button>
            <button title="Previous frame" onClick={async () => await appstate.play(PlayState.NextBackward)} disabled={appstate.playing.value != PlayState.None || appstate.historySize.value == 0 || appstate.playPointer.value <= 0} class="stepBtn">◃</button>
            <button title="Play backward" onClick={async () => await appstate.play(PlayState.Backward)} disabled={appstate.playing.value == PlayState.Backward || appstate.historySize.value == 0 || appstate.playPointer.value <= 0}>⏴︎</button>
            <button title="Pause" onClick={async () => await appstate.play(PlayState.None)} disabled={appstate.playing.value == PlayState.None || appstate.historySize.value == 0}>⏸︎</button>
            <button title="Play forward" onClick={async () => await appstate.play(PlayState.Forward)} disabled={appstate.playing.value == PlayState.Forward || appstate.playing.value == PlayState.ForwardWaiting || appstate.historySize.value == 0 || appstate.playPointer.value >= appstate.history.length - 1}>⏵︎</button>
            <button title="Next frame" onClick={async () => await appstate.play(PlayState.NextForward)} disabled={appstate.playing.value != PlayState.None || appstate.historySize.value == 0 || appstate.playPointer.value >= appstate.history.length - 1} class="stepBtn">▹</button>
            <button title="Seek to end" onClick={async () => await appstate.play(PlayState.SeekForward)} disabled={appstate.historySize.value == 0 || appstate.playPointer.value >= appstate.history.length - 1}> ⏭︎</button>
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
        {props.inclStats ? <>
            <div>Seeds: {appstate.seeds.value.length}</div>
            <div>Obstacles: {appstate.obstacles.value.length}</div>
            <div>Objects: {Math.floor(appstate.scene.value.reduce((a,c) => a + c.length, 0) + appstate.playPointer.value * 0.01)}</div>
            <div>Triangles: {Math.floor(appstate.scene.value.reduce((a,c) => a + c.reduce((b,d)=> b + tris[d.type], 0), 0) + appstate.playPointer.value * 0.01)}</div>
            <div>Backend Renderer: {appstate.renderer.value}</div>
        </> : <></>}
    </p>;
}