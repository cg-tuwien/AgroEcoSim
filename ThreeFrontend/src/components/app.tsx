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

const App = () => {
	return(
	<Fragment>
		<main id="app">
			<PlantsTable/>
			<ThreeSceneFn />
		</main>
		<nav id="hud">
			<HoursPerTick/>
			<TotalHours/>
			<FieldResolution/>
			<FieldCellsX/>
			<FieldCellsZ/>
			<FieldCellsD/>
			<Randomize/>
			<InitNumber/>
			<hr/>
			<Seeds/>
			<hr/>
			<Obstacles/>
			<hr/>
			<Start/>&nbsp;<ProgressBar/>
			<PlantsTable/>
		</nav>

	</Fragment>
)};

export default App;
