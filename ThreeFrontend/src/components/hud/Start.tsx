import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"
//import Button from 'preact-material-components/Button';
//import 'preact-material-components/Button/style.css';

export class Start extends Component
{
    render() {
        return <button onClick={async () => await appstate.run()}>{appstate.computing.value ? "(working)" : "Run"}</button>;
    }
}