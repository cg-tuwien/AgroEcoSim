import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"
//import Button from 'preact-material-components/Button';
//import 'preact-material-components/Button/style.css';

export function Start() {
    return <div><button onClick={async () => await appstate.run()}>{appstate.computing.value ? "Abort" : "Run"}</button></div>;
}