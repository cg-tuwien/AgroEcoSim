import { Component, h } from "preact";
import appstate, { PlayState } from "../../appstate";
//import "wired-elements"
//import Button from 'preact-material-components/Button';
//import 'preact-material-components/Button/style.css';

export function Start() {
    return <div>
        <button onClick={async () => await appstate.run()}>{appstate.computing.value ? "⏹︎" : "🚀"}</button>&nbsp;
        <button onClick={async () => await appstate.play(PlayState.SeekBackward)} disabled={appstate.historySize.value == 0 || appstate.playPointer.value <= 0}>⏮︎</button>
        <button onClick={async () => await appstate.play(PlayState.NextBackward)} disabled={appstate.playing.value != PlayState.None || appstate.historySize.value == 0 || appstate.playPointer.value <= 0} class="stepBtn">⏴︎</button>
        <button onClick={async () => await appstate.play(PlayState.Backward)} disabled={appstate.playing.value == PlayState.Backward || appstate.historySize.value == 0 || appstate.playPointer.value <= 0}>⏴︎</button>
        <button onClick={async () => await appstate.play(PlayState.None)} disabled={appstate.playing.value == PlayState.None || appstate.historySize.value == 0}>⏸︎</button>
        <button onClick={async () => await appstate.play(PlayState.Forward)} disabled={appstate.playing.value == PlayState.Forward || appstate.playing.value == PlayState.ForwardWaiting || appstate.historySize.value == 0 || appstate.playPointer.value >= appstate.history.length - 1}>⏵︎</button>
        <button onClick={async () => await appstate.play(PlayState.NextForward)} disabled={appstate.playing.value != PlayState.None || appstate.historySize.value == 0 || appstate.playPointer.value >= appstate.history.length - 1} class="stepBtn">⏵︎</button>
        <button onClick={async () => await appstate.play(PlayState.SeekForward)} disabled={appstate.historySize.value == 0 || appstate.playPointer.value >= appstate.history.length - 1}> ⏭︎</button>
    </div>;
}