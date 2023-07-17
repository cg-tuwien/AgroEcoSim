import { Component, h } from "preact";
import appstate, { PlayState } from "../../appstate";
import { batch } from "@preact/signals-core";
//import "wired-elements"
//import Button from 'preact-material-components/Button';
//import 'preact-material-components/Button/style.css';

export function Start() {
    return <div>
        <span>
            <button onClick={async () => await appstate.run()}>{appstate.computing.value ? "⏹︎" : "🚀"}</button>&nbsp;
            <button onClick={async () => await appstate.play(PlayState.SeekBackward)} disabled={appstate.historySize.value == 0 || appstate.playPointer.value <= 0}>⏮︎</button>
            <button onClick={async () => await appstate.play(PlayState.NextBackward)} disabled={appstate.playing.value != PlayState.None || appstate.historySize.value == 0 || appstate.playPointer.value <= 0} class="stepBtn">◃</button>
            <button onClick={async () => await appstate.play(PlayState.Backward)} disabled={appstate.playing.value == PlayState.Backward || appstate.historySize.value == 0 || appstate.playPointer.value <= 0}>⏴︎</button>
            <button onClick={async () => await appstate.play(PlayState.None)} disabled={appstate.playing.value == PlayState.None || appstate.historySize.value == 0}>⏸︎</button>
            <button onClick={async () => await appstate.play(PlayState.Forward)} disabled={appstate.playing.value == PlayState.Forward || appstate.playing.value == PlayState.ForwardWaiting || appstate.historySize.value == 0 || appstate.playPointer.value >= appstate.history.length - 1}>⏵︎</button>
            <button onClick={async () => await appstate.play(PlayState.NextForward)} disabled={appstate.playing.value != PlayState.None || appstate.historySize.value == 0 || appstate.playPointer.value >= appstate.history.length - 1} class="stepBtn">▹</button>
            <button onClick={async () => await appstate.play(PlayState.SeekForward)} disabled={appstate.historySize.value == 0 || appstate.playPointer.value >= appstate.history.length - 1}> ⏭︎</button>
        </span>
        &nbsp;
        <span>
            <input min={0} max={appstate.history.length - 1} type="range" name="frameslider" value={appstate.playPointer} onChange={e => {
                batch(() => {
                    appstate.playPointer.value = parseInt(e.currentTarget.value);
                    appstate.playRequest.value = true;
                    appstate.playing.value = PlayState.Seek;
                });
            }}/>
            <label for="frameslider">{appstate.playPointer.value}</label>
        </span>
    </div>;
}