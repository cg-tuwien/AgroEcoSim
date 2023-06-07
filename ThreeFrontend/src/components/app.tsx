import { Fragment, createContext, h } from 'preact';
import { Start } from './hud/Start'
import { HoursPerTick } from './hud/HoursPerTick';
import { TotalHours } from './hud/TotalHours';
import { FieldResolution } from './hud/FieldResolution';
import { FieldSizeX } from './hud/FieldSizeX';
import { FieldSizeZ } from './hud/FieldSizeZ';
import { FieldSizeD } from './hud/FieldSizeD';
import { InitNumber } from './hud/InitNumber';
import { PlantsTable } from './viewport/PlantsTable';
import { Randomize } from './hud/Randomize';
import { Seeds } from './hud/Seeds';
import ThreeSceneFn from './viewport/ThreeSceneFn';
import { ProgressBar } from './hud/ProgressBar';

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
			<FieldSizeX/>
			<FieldSizeZ/>
			<FieldSizeD/>
			<Randomize/>
			<InitNumber/>
			<hr/>
			<Seeds/>
			<hr/>
			<Start/>&nbsp;<ProgressBar/>
			<PlantsTable/>
		</nav>

	</Fragment>
)};

export default App;
