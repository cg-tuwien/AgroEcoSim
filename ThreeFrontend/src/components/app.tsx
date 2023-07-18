import { Fragment, createContext, h } from 'preact';
import { Start } from './hud/Start'
import { HoursPerTick } from './hud/HoursPerTick';
import { TotalHours } from './hud/TotalHours';
import { FieldResolution } from './hud/FieldResolution';
import { FieldCellsX } from './hud/FieldCellsX';
import { FieldCellsZ } from './hud/FieldCellsZ';
import { FieldCellsD } from './hud/FieldCellsD';
import { InitNumber } from './hud/InitNumber';
import { PlantsTable } from './viewport/PlantsTable';
import { Randomize } from './hud/Randomize';
import { Seeds } from './hud/Seeds';
import ThreeSceneFn from './viewport/ThreeSceneFn';
import { ProgressBar } from './hud/ProgressBar';
import { Obstacles } from './hud/Obstacles';
import { ConstantLight } from './hud/ConstantLight';
import { ExportImport } from './hud/ExportImport';
import { VisualMapping } from './hud/VisualMapping';
import { signal } from '@preact/signals';
//import {Tab, initTE } from "tw-elements"; initTE({ Tab }); //tried but failed

const tabs = signal("tab-home");

const App = () => {
	return(
	<Fragment>
		<main id="app">
			<ThreeSceneFn />
		</main>
		<nav id="hud">
			<ul role="tablist">
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-home"} aria-selected={tabs.value.endsWith("tab-home")}>Home</a></li>
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-sim"} aria-selected={tabs.value.endsWith("tab-sim")}>Simulation</a></li>
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-plants"} aria-selected={tabs.value.endsWith("tab-plants")}>Plants</a></li>
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-obstacles"} aria-selected={tabs.value.endsWith("tab-obstacles")}>Obstacles</a></li>
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-analysis"} aria-selected={tabs.value.endsWith("tab-analysis")}>Analysis</a></li>
				{() => "TODO: make this a separate panel always visible"}
			</ul>

			<div role="tabpanel" id="tab-home" aria-selected={tabs.value.endsWith("tab-home")}>
				<Start/>&nbsp;<ProgressBar/>
				<ExportImport/>
			</div>
			<div role="tabpanel" id="tab-sim" aria-selected={tabs.value.endsWith("tab-sim")}>
				<HoursPerTick/>
				<TotalHours/>
				<FieldResolution/>
				<FieldCellsX/>
				<FieldCellsZ/>
				<FieldCellsD/>
				<Randomize/>
				<InitNumber/>
				<ConstantLight/>
			</div>
			<div role="tabpanel" id="tab-plants" aria-selected={tabs.value.endsWith("tab-plants")}>
				<Seeds/>
			</div>
			<div role="tabpanel" id="tab-obstacles" aria-selected={tabs.value.endsWith("tab-obstacles")}>
				<Obstacles/>
			</div>
			<div role="tabpanel" id="tab-analysis" aria-selected={tabs.value.endsWith("tab-analysis")}>
				<VisualMapping/>
				<PlantsTable/>
			</div>
		</nav>
	</Fragment>
)};

export default App;
